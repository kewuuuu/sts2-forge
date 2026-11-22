using System;
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2Forge.Patching;

/// <summary>
/// Every patch is registered by hand and wrapped on its own: one missing vanilla member must never
/// take the rest of the patches (or the vanilla call chain) down with it.
/// </summary>
internal static class PatchApplier
{
	public static void ApplyAll()
	{
		Harmony harmony;
		try
		{
			harmony = new Harmony(Sts2ForgeMod.ModId);
		}
		catch (Exception e)
		{
			Log.Error(Sts2ForgeMod.LogPrefix + "harmony could not be created: " + e);
			return;
		}
		List<(string Name, Func<bool> Register)> steps = new List<(string Name, Func<bool> Register)>
		{
			("RestSiteOption.Generate", () => RestSitePatches.RegisterGenerate(harmony)),
			("RestSiteOption.Icon", () => RestSitePatches.RegisterIcon(harmony)),
			("RestSiteOption.AssetPaths", () => RestSitePatches.RegisterAssetPaths(harmony)),
			("AbstractModel.MutableClone", () => RestSitePatches.RegisterMutableClone(harmony)),
			("Hook.ModifyDamage", () => CombatPatches.RegisterModifyDamage(harmony)),
			("Hook.ModifyBlock", () => CombatPatches.RegisterModifyBlock(harmony)),
			("Hook.ModifyCardPlayCount", () => CombatPatches.RegisterModifyCardPlayCount(harmony)),
			("CardModel.GetDescriptionForPile", () => CardTextPatches.RegisterDescription(harmony)),
			("CardModel.HoverTips", () => CardTextPatches.RegisterHoverTips(harmony)),
			("CardModel.ToSerializable", () => SavePatches.RegisterToSerializable(harmony)),
			("CardModel.FromSerializable", () => SavePatches.RegisterFromSerializable(harmony)),
			("LocManager.Initialize", () => LocPatches.RegisterInitialize(harmony))
		};
		int applied = 0;
		foreach ((string name, Func<bool> register) in steps)
		{
			try
			{
				if (register())
				{
					applied++;
					Log.Info(Sts2ForgeMod.LogPrefix + "patch applied: " + name);
				}
				else
				{
					Log.Warn(Sts2ForgeMod.LogPrefix + "patch skipped (target not found): " + name);
				}
			}
			catch (Exception e)
			{
				Log.Warn(Sts2ForgeMod.LogPrefix + "patch skipped (exception): " + name + " -> " + e.GetType().Name + ": " + e.Message);
			}
		}
		Log.Info(Sts2ForgeMod.LogPrefix + "harmony patches applied: " + applied + "/" + steps.Count);
	}
}
