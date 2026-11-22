using System;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using Sts2Forge.Combat;
using Sts2Forge.Config;
using Sts2Forge.Core;
using Sts2Forge.Localization;
using Sts2Forge.Net;
using Sts2Forge.Patching;

namespace Sts2Forge;

[ModInitializer("Init")]
public static class Sts2ForgeMod
{
	public const string ModId = "sts2-forge";
	public const string LogPrefix = "[sts2-forge] ";
	public const string Version = "0.1.0-beta";

	public static void Init()
	{
		try
		{
			Log.Info(LogPrefix + "loading v" + Version);
			ForgeConfigStore.Load();
			PatchApplier.ApplyAll();
			SubscribeCombatHooks();
			ForgeLoc.TryInjectNow();
			RunManager.Instance.RunStarted += OnRunStarted;
			RunManager.Instance.RoomEntered += OnRoomEntered;
#if RITSULIB
			Integration.RitsuLibSettingsPage.TryRegister();
#endif
			ForgeConfig effective = ForgeConfigStore.Effective;
			Log.Info(LogPrefix + "initialized; success_percent=" + effective.SuccessPercent
				+ ", consume_rest_action=" + BoolText(effective.ConsumeRestAction)
				+ ", one_forge_per_rest=" + BoolText(effective.OneForgePerRest)
				+ ", log_debug=" + BoolText(effective.LogDebug));
		}
		catch (Exception e)
		{
			Log.Error(LogPrefix + "init failed: " + e);
		}
	}

	internal static string BoolText(bool value)
	{
		return value ? "true" : "false";
	}

	private static void SubscribeCombatHooks()
	{
		try
		{
			// Mod init runs before ModelDb.Init (OneTimeInitialization.cs:59 vs :81), and a model type that nothing else
			// references is registered only if the subtype scan already cached it. Inject keeps the listener resolvable.
			ModelDb.Inject(typeof(ForgeCombatListener));
			ModHelper.SubscribeForCombatStateHooks(ModId, ForgeCombatListener.ProvideForCombat);
			Log.Info(LogPrefix + "combat hook listener subscribed (model resolves on first combat hook)");
		}
		catch (Exception e)
		{
			Log.Warn(LogPrefix + "combat hook subscription failed: " + e.Message);
		}
	}

	private static void OnRunStarted(RunState state)
	{
		try
		{
			ForgeAttempts.Reset();
			ForgeRuntimeConfig.ClearOverride();
			ForgeRuntimeConfig.EndRun();
			ForgeConfigSync.AttachForRun(state);
			if (!ForgeRuntimeConfig.IsMultiplayerClient)
			{
				ForgeRuntimeConfig.BeginRun(ForgeRuntimeConfig.Effective);
			}
			Log.Info(LogPrefix + "run started; for_this_run=" + ForgeRuntimeConfig.ForThisRun.Fingerprint()
				+ "; combat_listener=" + BoolText(ForgeCombatListener.TryGet() != null));
		}
		catch (Exception e)
		{
			Log.Warn(LogPrefix + "run start handling failed: " + e.Message);
		}
	}

	private static void OnRoomEntered()
	{
		try
		{
			ForgeConfigSync.RequestConfigIfMissing();
		}
		catch (Exception e)
		{
			Log.Warn(LogPrefix + "room entered handling failed: " + e.Message);
		}
	}
}
