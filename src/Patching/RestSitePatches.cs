using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using Sts2Forge.Art;
using Sts2Forge.Core;
using Sts2Forge.Entities;

namespace Sts2Forge.Patching;

internal static class RestSitePatches
{
	internal static bool RegisterGenerate(Harmony harmony)
	{
		MethodInfo? target = AccessTools.Method(typeof(RestSiteOption), nameof(RestSiteOption.Generate), new[] { typeof(Player) });
		if (target == null)
		{
			return false;
		}
		harmony.Patch(target, postfix: new HarmonyMethod(typeof(RestSitePatches), nameof(GeneratePostfix)));
		return true;
	}

	internal static bool RegisterIcon(Harmony harmony)
	{
		MethodInfo? target = AccessTools.Method(typeof(NRestSiteButton), "Reload");
		if (target == null)
		{
			return false;
		}
		harmony.Patch(target, transpiler: new HarmonyMethod(typeof(RestSitePatches), nameof(ReloadIconTranspiler)));
		return true;
	}

	internal static bool RegisterAssetPaths(Harmony harmony)
	{
		MethodInfo? target = AccessTools.PropertyGetter(typeof(RestSiteOption), nameof(RestSiteOption.AssetPaths));
		if (target == null)
		{
			return false;
		}
		harmony.Patch(target, prefix: new HarmonyMethod(typeof(RestSitePatches), nameof(AssetPathsPrefix)));
		return true;
	}

	internal static bool RegisterMutableClone(Harmony harmony)
	{
		MethodInfo? target = AccessTools.Method(typeof(AbstractModel), nameof(AbstractModel.MutableClone));
		if (target == null)
		{
			return false;
		}
		harmony.Patch(target, postfix: new HarmonyMethod(typeof(RestSitePatches), nameof(MutableClonePostfix)));
		return true;
	}

	internal static bool RegisterOptionRow(Harmony harmony)
	{
		MethodInfo? target = AccessTools.Method(typeof(NRestSiteRoom), "UpdateRestSiteOptions");
		if (target == null)
		{
			return false;
		}
		harmony.Patch(target, postfix: new HarmonyMethod(typeof(RestSitePatches), nameof(OptionRowPostfix)));
		return true;
	}

	private static void OptionRowPostfix(NRestSiteRoom __instance)
	{
		try
		{
			if (__instance == null || !__instance.IsInsideTree())
			{
				return;
			}
			Callable.From(delegate
			{
				FitOptionRow(__instance);
			}).CallDeferred();
		}
		catch (Exception ex)
		{
			Log.Warn(Sts2ForgeMod.LogPrefix + "option row postfix failed: " + ex.Message);
		}
	}

	private static void FitOptionRow(NRestSiteRoom room)
	{
		try
		{
			if (room == null || !room.IsInsideTree())
			{
				return;
			}
			if (!(AccessTools.Field(typeof(NRestSiteRoom), "_choicesContainer")?.GetValue(room) is Control row))
			{
				return;
			}
			int count = row.GetChildCount();
			if (count < 2)
			{
				return;
			}
			float width = 0f;
			for (int i = 0; i < count; i++)
			{
				if (row.GetChild(i) is Control child)
				{
					width = Mathf.Max(width, child.Position.X + child.Size.X);
				}
			}
			float avail = row.Size.X;
			if (row.GetParent() is Control parent && parent.Size.X > 0f)
			{
				avail = Mathf.Max(avail, parent.Size.X - 64f);
			}
			float fit = (width > 1f && avail > 1f) ? Mathf.Min(1f, avail / width) : 1f;
			row.PivotOffset = row.Size * 0.5f;
			row.Scale = new Vector2(fit, fit);
		}
		catch (Exception ex)
		{
			Log.Warn(Sts2ForgeMod.LogPrefix + "fit option row failed: " + ex.Message);
		}
	}

	private static void GeneratePostfix(Player player, ref List<RestSiteOption> __result)
	{
		try
		{
			if (__result == null)
			{
				__result = new List<RestSiteOption>();
			}
			if (__result.Any(option => option is ForgeRestSiteOption))
			{
				return;
			}
			ForgeRestSiteOption forge = new ForgeRestSiteOption(player);
			int smithIndex = __result.FindIndex(option => string.Equals(option.OptionId, "SMITH", StringComparison.Ordinal));
			if (smithIndex >= 0)
			{
				__result.Insert(smithIndex + 1, forge);
			}
			else
			{
				__result.Add(forge);
			}
			Log.Info(Sts2ForgeMod.LogPrefix + "forge option offered; enabled=" + Sts2ForgeMod.BoolText(forge.IsEnabled)
				+ ", options=" + string.Join(",", __result.Select(option => option.OptionId)));
		}
		catch (Exception e)
		{
			Log.Warn(Sts2ForgeMod.LogPrefix + "forge option could not be added: " + e.Message);
		}
	}

	private static IEnumerable<CodeInstruction> ReloadIconTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo getter = AccessTools.PropertyGetter(typeof(RestSiteOption), nameof(RestSiteOption.Icon));
		MethodInfo replacement = AccessTools.Method(typeof(RestSitePatches), nameof(GetActionIcon));
		bool replaced = false;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(getter))
			{
				instruction.opcode = OpCodes.Call;
				instruction.operand = replacement;
				replaced = true;
			}
			yield return instruction;
		}
		if (!replaced)
		{
			throw new InvalidOperationException("Rest site button icon call was not found.");
		}
	}

	private static Texture2D GetActionIcon(RestSiteOption option) =>
		option is ForgeRestSiteOption && ForgeArt.RestSiteIconBig is { } art ? art : option.Icon;

	private static bool AssetPathsPrefix(RestSiteOption __instance, ref IEnumerable<string> __result)
	{
		try
		{
			if (__instance is not ForgeRestSiteOption)
			{
				return true;
			}
			__result = Array.Empty<string>();
			return false;
		}
		catch (Exception)
		{
			return true;
		}
	}

	private static void MutableClonePostfix(AbstractModel __instance, AbstractModel __result)
	{
		try
		{
			if (__instance is CardModel source && __result is CardModel clone && !ReferenceEquals(source, clone))
			{
				ForgeLevels.CopyLevel(source, clone);
			}
		}
		catch (Exception e)
		{
			Log.Warn(Sts2ForgeMod.LogPrefix + "forge level copy on clone failed: " + e.Message);
		}
	}
}
