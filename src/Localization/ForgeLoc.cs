using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2Forge.Localization;

/// <summary>
/// DLL-only mods ship no pck, so the strings are merged into the live tables after LocManager.Initialize.
/// Rest site option titles and descriptions are read from the vanilla "rest_site_ui" table by key.
/// </summary>
public static class ForgeLoc
{
	private const string ForgeTable = "forge_ui";

	private const string RestSiteTable = "rest_site_ui";

	private static readonly Dictionary<string, string> English = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		["OPTION_FORGE.name"] = "Forge",
		["OPTION_FORGE.description"] = "Consume copies of a card to forge it: success raises its forge level, failure lowers it. Either way this rest is used up.",
		["OPTION_FORGE.descriptionDisabled"] = "No attack, skill, power or curse below forge level 5 in your deck.",
		["FORGE.CARD_LABEL"] = "Forged",
		["FORGE.SELECT_MAIN"] = "Choose a card to forge (success {percent}%, consumed cards are not refunded)",
		["FORGE.SELECT_MAIN_P0"] = "Choose a card to forge (success 0%: this attempt is doomed)",
		["FORGE.SELECT_PAD"] = "Optional: pick a pad card of the same forge level. A failed forge consumes the pad instead of dropping a level. No pad? Select nothing and press confirm (or use the back arrow)",
		["FORGE.TIP_LEVEL"] = "Forge level: {0} (+1 per forge, max 5)",
		["FORGE.TIP_DAMAGE"] = "Attack: +3 damage",
		["FORGE.TIP_BLOCK"] = "Skill: +3 block",
		["FORGE.TIP_DRAW"] = "Power: draw 1 card when played",
		["FORGE.TIP_CURSE"] = "Curse: playable for 0 energy, goes to the discard pile",
		["FORGE.TIP_ENERGY"] = "Gain 1 energy when played",
		["FORGE.TIP_REPLAY"] = "Played one extra time (multiplies with replay)",
		["FORGE.TIP_MIRACLE"] = "Gain 1 Miracle when played (negates the next life loss)",
		["FORGE.NOT_ENOUGH"] = "Not enough same-name copies for this forge",
		["FORGE.NOT_ENOUGH_BODY"] = "{card} needs {need} same-name copies, your deck has {have}",
		["FORGE.RESULT_SUCCESS_BODY"] = "{card}: forge level {from} -> {to}, {used} same-name copies consumed",
		["FORGE.RESULT_FAIL_BODY"] = "{card}: forge level {from} -> {to}, {used} same-name copies consumed with no refund",
		["FORGE.RESULT_FAIL_PAD_BODY"] = "{card}: forge level stays at {from}, {used} same-name copies and the pad {pad} are consumed",
		["FORGE.RESULT_OK"] = "OK",
		["FORGE.NOT_FORGEABLE"] = "That card cannot be forged",
		["FORGE.SUCCESS"] = "Forge succeeded",
		["FORGE.FAIL"] = "Forge failed",
		["FORGE.FAIL_PADDED"] = "Forge failed, the pad absorbed the loss",
		["FORGE.CANCELLED"] = "Forge cancelled"
	};

	private static readonly Dictionary<string, string> Chinese = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		["OPTION_FORGE.name"] = "锻铸",
		["OPTION_FORGE.description"] = "消耗同名牌锻铸一张牌：成功提升锻铸等级，失败降低一级。无论成败都会用掉这次休息。",
		["OPTION_FORGE.descriptionDisabled"] = "牌组里没有锻铸等级未满 5 的攻击、技能、能力或诅咒牌。",
		["FORGE.CARD_LABEL"] = "锻铸",
		["FORGE.SELECT_MAIN"] = "选择要锻铸的牌（成功率 {percent}%，失败不退材料）",
		["FORGE.SELECT_MAIN_P0"] = "选择要锻铸的牌（成功率 0%：本次必定失败）",
		["FORGE.SELECT_PAD"] = "可选：选一张同锻铸等级的垫子。失败时它替你挡住降级（会被消耗）。不用垫子就不选牌，直接按确认（或左上角返回）",
		["FORGE.TIP_LEVEL"] = "锻铸等级：{0}（每次锻铸 +1，上限 5）",
		["FORGE.TIP_DAMAGE"] = "攻击牌：伤害 +3",
		["FORGE.TIP_BLOCK"] = "技能牌：格挡 +3",
		["FORGE.TIP_DRAW"] = "能力牌打出时抽 1 张牌",
		["FORGE.TIP_CURSE"] = "诅咒牌可以打出（0 费，进弃牌堆）",
		["FORGE.TIP_ENERGY"] = "打出时获得 1 点能量",
		["FORGE.TIP_REPLAY"] = "打出时额外再打一次（与重放次数相乘）",
		["FORGE.TIP_MIRACLE"] = "打出时获得 1 层奇迹（抵消下一次生命损失）",
		["FORGE.NOT_ENOUGH"] = "同名牌不足，无法锻铸",
		["FORGE.NOT_ENOUGH_BODY"] = "{card} 需要 {need} 张同名牌，牌组里只有 {have} 张",
		["FORGE.RESULT_SUCCESS_BODY"] = "{card}：锻铸等级 {from} → {to}，消耗 {used} 张同名牌",
		["FORGE.RESULT_FAIL_BODY"] = "{card}：锻铸等级 {from} → {to}，消耗 {used} 张同名牌，不退",
		["FORGE.RESULT_FAIL_PAD_BODY"] = "{card}：锻铸等级保持 {from}，消耗 {used} 张同名牌和垫子 {pad}",
		["FORGE.RESULT_OK"] = "确定",
		["FORGE.NOT_FORGEABLE"] = "这张牌不能锻铸",
		["FORGE.SUCCESS"] = "锻铸成功",
		["FORGE.FAIL"] = "锻铸失败",
		["FORGE.FAIL_PADDED"] = "锻铸失败，垫子挡住了降级",
		["FORGE.CANCELLED"] = "已取消锻铸"
	};

	public static void InjectAll()
	{
		try
		{
			LocManager? manager = LocManager.Instance;
			if (manager == null)
			{
				Log.Info(Sts2ForgeMod.LogPrefix + "locale manager not ready yet; strings are injected from the LocManager.Initialize hook");
				return;
			}
			Dictionary<string, string> entries = new Dictionary<string, string>(StringComparer.Ordinal);
			Dictionary<string, string> restSiteEntries = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (KeyValuePair<string, string> pair in English)
			{
				if (pair.Key.StartsWith("OPTION_FORGE.", StringComparison.Ordinal))
				{
					restSiteEntries[pair.Key] = pair.Value;
				}
				else
				{
					entries[pair.Key] = pair.Value;
				}
			}
			if (IsChinese(manager))
			{
				foreach (KeyValuePair<string, string> pair in Chinese)
				{
					if (pair.Key.StartsWith("OPTION_FORGE.", StringComparison.Ordinal))
					{
						restSiteEntries[pair.Key] = pair.Value;
					}
					else
					{
						entries[pair.Key] = pair.Value;
					}
				}
			}
			Inject(manager, ForgeTable, entries);
			Inject(manager, RestSiteTable, restSiteEntries);
			Log.Info(Sts2ForgeMod.LogPrefix + "localization injected: " + entries.Count + " forge keys + " + restSiteEntries.Count + " rest site keys (culture=" + manager.CultureInfo.Name + ")");
		}
		catch (Exception e)
		{
			Log.Warn(Sts2ForgeMod.LogPrefix + "localization injection failed: " + e.Message);
		}
	}

	public static void TryInjectNow()
	{
		InjectAll();
	}

	public static string Text(string key, string fallback)
	{
		return Core.ForgeLevels.Loc(key, fallback);
	}

	private static void Inject(LocManager manager, string table, Dictionary<string, string> entries)
	{
		Dictionary<string, string> copy = new Dictionary<string, string>(entries, StringComparer.Ordinal);
		try
		{
			manager.GetTable(table).MergeWith(copy);
			return;
		}
		catch (LocException)
		{
		}
		catch (Exception e)
		{
			Log.Warn(Sts2ForgeMod.LogPrefix + "localization table '" + table + "' not injected: " + e.Message);
			return;
		}
		RegisterTable(manager, table, copy);
	}

	/// <summary>
	/// A table that no vanilla locale file defines is absent from LocManager's private table map, and GetTable never
	/// creates one (LocManager.cs:513-521). Register our own LocTable (LocTable.cs:20-25) so the keys resolve, and rely
	/// on the locale-change subscription to re-add it after a language switch rebuilds the map (LocManager.cs:374).
	/// </summary>
	private static void RegisterTable(LocManager manager, string table, Dictionary<string, string> entries)
	{
		try
		{
			FieldInfo? field = AccessTools.Field(typeof(LocManager), "_tables");
			if (field?.GetValue(manager) is Dictionary<string, LocTable> tables)
			{
				tables[table] = new LocTable(table, entries);
				Log.Info(Sts2ForgeMod.LogPrefix + "localization table '" + table + "' created with " + entries.Count + " keys");
				return;
			}
			Log.Warn(Sts2ForgeMod.LogPrefix + "localization table '" + table + "' not registered: table map unavailable");
		}
		catch (Exception e)
		{
			Log.Warn(Sts2ForgeMod.LogPrefix + "localization table '" + table + "' not registered: " + e.Message);
		}
	}

	private static bool IsChinese(LocManager manager)
	{
		return manager.CultureInfo.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
	}
}
