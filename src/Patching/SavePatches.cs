using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;
using Sts2Forge.Core;

namespace Sts2Forge.Patching;

/// <summary>
/// The level rides inside SerializableCard.Props as an int ("forge_level"); the keyword state is not
/// saved and is rebuilt from that int when the card is deserialized.
/// </summary>
internal static class SavePatches
{
	internal static bool RegisterToSerializable(Harmony harmony)
	{
		MethodInfo? target = AccessTools.Method(typeof(CardModel), nameof(CardModel.ToSerializable));
		if (target == null)
		{
			return false;
		}
		harmony.Patch(target, postfix: new HarmonyMethod(typeof(SavePatches), nameof(ToSerializablePostfix)));
		return true;
	}

	internal static bool RegisterFromSerializable(Harmony harmony)
	{
		MethodInfo? target = AccessTools.Method(typeof(CardModel), nameof(CardModel.FromSerializable));
		if (target == null)
		{
			return false;
		}
		harmony.Patch(target, postfix: new HarmonyMethod(typeof(SavePatches), nameof(FromSerializablePostfix)));
		return true;
	}

	private static void ToSerializablePostfix(CardModel __instance, SerializableCard __result)
	{
		try
		{
			int level = ForgeLevels.GetLevel(__instance);
			if (level <= 0 || __result == null)
			{
				return;
			}
			SavedProperties props = __result.Props ?? new SavedProperties();
			props.ints ??= new List<SavedProperties.SavedProperty<int>>();
			props.ints.RemoveAll(entry => string.Equals(entry.name, ForgeLevels.SaveKey, StringComparison.Ordinal));
			props.ints.Add(new SavedProperties.SavedProperty<int>(ForgeLevels.SaveKey, level));
			__result.Props = props;
		}
		catch (Exception e)
		{
			Log.Warn(Sts2ForgeMod.LogPrefix + "forge level save failed: " + e.Message);
		}
	}

	private static void FromSerializablePostfix(SerializableCard save, CardModel __result)
	{
		try
		{
			if (__result == null)
			{
				return;
			}
			int level = ReadLevel(save?.Props);
			if (level <= 0)
			{
				return;
			}
			ForgeLevels.SetLevel(__result, level);
			if (level >= ForgeLevels.LevelCursePlayable)
			{
				Log.Debug(Sts2ForgeMod.LogPrefix + "restored forge level " + level + " on " + __result.Id.Entry);
			}
		}
		catch (Exception e)
		{
			Log.Warn(Sts2ForgeMod.LogPrefix + "forge level load failed: " + e.Message);
		}
	}

	private static int ReadLevel(SavedProperties? props)
	{
		if (props?.ints == null)
		{
			return 0;
		}
		int level = 0;
		foreach (SavedProperties.SavedProperty<int> entry in props.ints)
		{
			if (string.Equals(entry.name, ForgeLevels.SaveKey, StringComparison.Ordinal))
			{
				level = entry.value;
			}
		}
		return level;
	}
}
