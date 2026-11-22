using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using Sts2Forge.Localization;

namespace Sts2Forge.Patching;

/// <summary>A DLL-only mod has no pck, so strings are merged into the live tables after the locale loads.</summary>
internal static class LocPatches
{
	internal static bool RegisterInitialize(Harmony harmony)
	{
		MethodInfo? target = AccessTools.Method(typeof(LocManager), "Initialize");
		if (target == null)
		{
			return false;
		}
		harmony.Patch(target, postfix: new HarmonyMethod(typeof(LocPatches), nameof(InitializePostfix)));
		return true;
	}

	private static void InitializePostfix()
	{
		try
		{
			ForgeLoc.InjectAll();
			LocManager? manager = LocManager.Instance;
			manager?.SubscribeToLocaleChange(ForgeLoc.InjectAll);
		}
		catch (Exception e)
		{
			Log.Warn(Sts2ForgeMod.LogPrefix + "localization bootstrap failed: " + e.Message);
		}
	}
}
