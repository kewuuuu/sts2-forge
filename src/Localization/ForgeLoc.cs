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
		["FORGE.TITLE"] = "Forge",
		["FORGE.TARGET"] = "Choose one target. It stays fixed until you finish forging.",
		["FORGE.PROBABILITY"] = "Success chance: {0}% · Materials are never refunded",
		["FORGE.HISTORY"] = "Round results",
		["FORGE.FINISH"] = "End forging",
		["FORGE.COMMIT"] = "Forge this round",
		["FORGE.MAX"] = "This card is at maximum forge level.",
		["FORGE.MATERIALS"] = "Select exactly {0} matching cards ({1}/{0} selected).",
		["FORGE.SHORTAGE"] = "Need {0} matching cards. Only {1} can be used.",
		["FORGE.ROUND_SUCCESS"] = "Round {0} · Success · {1} → {2} · Used {3} cards",
		["FORGE.ROUND_FAIL"] = "Round {0} · Failed · {1} → {2} · Used {3} cards",
		["OPTION_FORGE.name"] = "Forge",
		["OPTION_FORGE.description"] = "Forge one card over multiple rounds using matching copies. Success raises its forge level and failure lowers it.",
		["OPTION_FORGE.descriptionDisabled"] = "No attack, skill, power or curse below forge level 5 in your deck.",
		["FORGE.CARD_LABEL"] = "Forged",
		["FORGE.TIP_LEVEL"] = "Forge level: {0} (+1 per forge, max 5)",
		["FORGE.TIP_DAMAGE"] = "Attack: +3 damage",
		["FORGE.TIP_BLOCK"] = "Skill: +3 block",
		["FORGE.TIP_DRAW"] = "Power: draw 1 card when played",
		["FORGE.TIP_CURSE"] = "Curse: playable for 0 energy, goes to the discard pile",
		["FORGE.TIP_ENERGY"] = "Gain 1 energy when played",
		["FORGE.TIP_REPLAY"] = "Played one extra time (multiplies with replay)",
		["FORGE.TIP_MIRACLE"] = "Gain 1 Miracle when played (negates the next life loss)",
	};

	private static readonly Dictionary<string, string> Chinese = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		["FORGE.TITLE"] = "锻铸",
		["FORGE.TARGET"] = "选择一张要强化的牌。选定后，结束锻铸前不能更换。",
		["FORGE.PROBABILITY"] = "成功率 {0}% · 消耗的材料不会退还",
		["FORGE.HISTORY"] = "每轮结果",
		["FORGE.FINISH"] = "结束锻铸",
		["FORGE.COMMIT"] = "开始本轮锻铸",
		["FORGE.MAX"] = "这张牌的锻铸等级已满。",
		["FORGE.MATERIALS"] = "请选择恰好 {0} 张同名牌（已选 {1}/{0} 张）。",
		["FORGE.SHORTAGE"] = "需要 {0} 张同名牌，只有 {1} 张可用。",
		["FORGE.ROUND_SUCCESS"] = "第 {0} 轮 · 成功 · {1} → {2} · 消耗 {3} 张",
		["FORGE.ROUND_FAIL"] = "第 {0} 轮 · 失败 · {1} → {2} · 消耗 {3} 张",
		["OPTION_FORGE.name"] = "锻铸",
		["OPTION_FORGE.description"] = "消耗同名牌，连续锻铸同一张牌。成功升一级，失败降一级。",
		["OPTION_FORGE.descriptionDisabled"] = "牌组里没有锻铸等级未满 5 的攻击、技能、能力或诅咒牌。",
		["FORGE.CARD_LABEL"] = "锻铸",
		["FORGE.TIP_LEVEL"] = "锻铸等级：{0}（每次锻铸 +1，上限 5）",
		["FORGE.TIP_DAMAGE"] = "攻击牌：伤害 +3",
		["FORGE.TIP_BLOCK"] = "技能牌：格挡 +3",
		["FORGE.TIP_DRAW"] = "能力牌打出时抽 1 张牌",
		["FORGE.TIP_CURSE"] = "诅咒牌可以打出（0 费，进弃牌堆）",
		["FORGE.TIP_ENERGY"] = "打出时获得 1 点能量",
		["FORGE.TIP_REPLAY"] = "打出时额外再打一次（与重放次数相乘）",
		["FORGE.TIP_MIRACLE"] = "打出时获得 1 层奇迹（抵消下一次生命损失）",
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
