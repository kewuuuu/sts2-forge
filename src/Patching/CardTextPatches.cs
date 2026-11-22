using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using Sts2Forge.Core;

namespace Sts2Forge.Patching;

/// <summary>Card face and hover tip: the forge line is appended, never added as a vanilla keyword.</summary>
internal static class CardTextPatches
{
	internal static bool RegisterDescription(Harmony harmony)
	{
		Type? previewType = typeof(CardModel).GetNestedType("DescriptionPreviewType", BindingFlags.NonPublic);
		if (previewType == null)
		{
			return false;
		}
		MethodInfo? target = AccessTools.Method(typeof(CardModel), "GetDescriptionForPile", new[] { typeof(PileType), previewType, typeof(Creature) });
		if (target == null)
		{
			return false;
		}
		harmony.Patch(target, postfix: new HarmonyMethod(typeof(CardTextPatches), nameof(DescriptionPostfix)));
		return true;
	}

	internal static bool RegisterHoverTips(Harmony harmony)
	{
		MethodInfo? target = AccessTools.PropertyGetter(typeof(CardModel), nameof(CardModel.HoverTips));
		if (target == null)
		{
			return false;
		}
		harmony.Patch(target, postfix: new HarmonyMethod(typeof(CardTextPatches), nameof(HoverTipsPostfix)));
		return true;
	}

	private static void DescriptionPostfix(CardModel __instance, ref string __result)
	{
		try
		{
			int level = ForgeLevels.GetLevel(__instance);
			if (level <= 0)
			{
				return;
			}
			string line = "[gold]" + ForgeLevels.GetLevelText(level) + "[/gold]";
			__result = string.IsNullOrEmpty(__result) ? line : __result + "\n" + line;
		}
		catch (Exception e)
		{
			Log.Warn(Sts2ForgeMod.LogPrefix + "card description patch failed: " + e.Message);
		}
	}

	private static void HoverTipsPostfix(CardModel __instance, ref IEnumerable<IHoverTip> __result)
	{
		try
		{
			int level = ForgeLevels.GetLevel(__instance);
			if (level <= 0)
			{
				return;
			}
			List<IHoverTip> tips = __result == null ? new List<IHoverTip>() : new List<IHoverTip>(__result);
			LocString title = new LocString(ForgeLevels.LocTable, "FORGE.CARD_LABEL");
			tips.Add(new HoverTip(title, ForgeLevels.BuildHoverTipBody(level)));
			__result = tips;
		}
		catch (Exception e)
		{
			Log.Warn(Sts2ForgeMod.LogPrefix + "hover tip patch failed: " + e.Message);
		}
	}
}
