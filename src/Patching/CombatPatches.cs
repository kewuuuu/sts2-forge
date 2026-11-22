using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using Sts2Forge.Core;

namespace Sts2Forge.Patching;

/// <summary>
/// Level 2 damage and block bonuses live in the same two functions the previews use, so the number on the card
/// and the number the game applies cannot drift apart.
/// </summary>
internal static class CombatPatches
{
	internal static bool RegisterModifyDamage(Harmony harmony)
	{
		MethodInfo? target = AccessTools.Method(typeof(Hook), "ModifyDamage");
		if (target == null)
		{
			return false;
		}
		harmony.Patch(target, prefix: new HarmonyMethod(typeof(CombatPatches), nameof(ModifyDamagePrefix)));
		return true;
	}

	internal static bool RegisterModifyBlock(Harmony harmony)
	{
		MethodInfo? target = AccessTools.Method(typeof(Hook), "ModifyBlock");
		if (target == null)
		{
			return false;
		}
		harmony.Patch(target, prefix: new HarmonyMethod(typeof(CombatPatches), nameof(ModifyBlockPrefix)));
		return true;
	}

	internal static bool RegisterModifyCardPlayCount(Harmony harmony)
	{
		MethodInfo? target = AccessTools.Method(typeof(Hook), "ModifyCardPlayCount");
		if (target == null)
		{
			return false;
		}
		harmony.Patch(target, postfix: new HarmonyMethod(typeof(CombatPatches), nameof(ModifyCardPlayCountPostfix)));
		return true;
	}

	private static void ModifyDamagePrefix(CardModel? cardSource, ref decimal damage)
	{
		try
		{
			if (damage <= 0m || !ForgeLevels.GivesDamageBonus(cardSource))
			{
				return;
			}
			damage += ForgeLevels.DamageBonus;
		}
		catch (Exception e)
		{
			Log.Warn(Sts2ForgeMod.LogPrefix + "damage hook failed: " + e.Message);
		}
	}

	private static void ModifyBlockPrefix(CardModel? cardSource, ref decimal block)
	{
		try
		{
			if (block <= 0m || !ForgeLevels.GivesBlockBonus(cardSource))
			{
				return;
			}
			block += ForgeLevels.BlockBonus;
		}
		catch (Exception e)
		{
			Log.Warn(Sts2ForgeMod.LogPrefix + "block hook failed: " + e.Message);
		}
	}

	private static void ModifyCardPlayCountPostfix(CardModel card, ref int __result)
	{
		try
		{
			if (__result <= 0 || ForgeLevels.GetLevel(card) < ForgeLevels.LevelExtraPlay)
			{
				return;
			}
			__result *= 2;
		}
		catch (Exception e)
		{
			Log.Warn(Sts2ForgeMod.LogPrefix + "play count hook failed: " + e.Message);
		}
	}
}
