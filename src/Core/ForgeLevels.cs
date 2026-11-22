using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;

namespace Sts2Forge.Core;

/// <summary>
/// Forge levels are kept next to the live card model. The save file only ever sees an int
/// (SerializableCard.Props entry "forge_level"), so every read/write of the level goes through here.
/// </summary>
public static class ForgeLevels
{
	public const int MinLevel = 0;
	public const int MaxLevel = 5;

	public const int DamageBonus = 3;
	public const int BlockBonus = 3;

	public const int LevelDrawOnPower = 2;
	public const int LevelCursePlayable = 2;
	public const int LevelEnergyOnPlay = 3;
	public const int LevelExtraPlay = 4;
	public const int LevelMiracleOnPlay = 5;

	public const int CardsDrawnOnPower = 1;
	public const decimal EnergyPerPlay = 1m;
	public const decimal MiraclePerPlay = 1m;

	public const string SaveKey = "forge_level";
	public const string LocTable = "forge_ui";

	private static readonly ConditionalWeakTable<CardModel, ForgeLevelBox> Levels = new ConditionalWeakTable<CardModel, ForgeLevelBox>();

	private sealed class ForgeLevelBox
	{
		public int Level;

		public ForgeLevelBox(int level)
		{
			Level = level;
		}
	}

	public static int GetLevel(CardModel? card)
	{
		if (card == null)
		{
			return MinLevel;
		}
		return Levels.TryGetValue(card, out ForgeLevelBox? box) ? box.Level : MinLevel;
	}

	public static bool HasLevel(CardModel? card)
	{
		return GetLevel(card) > MinLevel;
	}

	public static bool IsForgeable(CardModel? card)
	{
		if (card == null || GetLevel(card) >= MaxLevel)
		{
			// A max-level card has nothing left to gain: a successful roll would consume (level + 1)
			// copies and stay at MaxLevel, and a failed one would drop it back down. Never offer it.
			return false;
		}
		return card.Type switch
		{
			CardType.Attack => true,
			CardType.Skill => true,
			CardType.Power => true,
			CardType.Curse => true,
			_ => false
		};
	}

	public static bool GivesDamageBonus(CardModel? card)
	{
		return card != null && GetLevel(card) >= 1 && card.Type == CardType.Attack;
	}

	public static bool GivesBlockBonus(CardModel? card)
	{
		return card != null && GetLevel(card) >= 1 && card.Type == CardType.Skill;
	}

	public static bool NeedsCurseUnplayableRemoved(CardModel? card)
	{
		return card != null
			&& GetLevel(card) >= LevelCursePlayable
			&& card.Type == CardType.Curse
			&& card.CanonicalKeywords.Contains(CardKeyword.Unplayable);
	}

	public static void SetLevel(CardModel card, int level)
	{
		if (card == null)
		{
			return;
		}
		int clamped = Math.Clamp(level, MinLevel, MaxLevel);
		if (Levels.TryGetValue(card, out ForgeLevelBox? box))
		{
			box.Level = clamped;
		}
		else
		{
			Levels.Add(card, new ForgeLevelBox(clamped));
		}
		ApplyKeywordState(card, clamped);
	}

	public static void CopyLevel(CardModel source, CardModel target)
	{
		if (source == null || target == null)
		{
			return;
		}
		int level = GetLevel(source);
		if (level <= MinLevel)
		{
			return;
		}
		SetLevel(target, level);
	}

	/// <summary>
	/// Keywords are not saved, so the "curse is playable at level 2+" rule is re-applied from the level
	/// after every load, clone and level change.
	/// </summary>
	public static void ApplyKeywordState(CardModel card, int level)
	{
		try
		{
			if (!card.CanonicalKeywords.Contains(CardKeyword.Unplayable))
			{
				return;
			}
			bool playable = level >= LevelCursePlayable;
			if (playable)
			{
				if (card.Keywords.Contains(CardKeyword.Unplayable))
				{
					card.RemoveKeyword(CardKeyword.Unplayable);
				}
			}
			else if (!card.Keywords.Contains(CardKeyword.Unplayable))
			{
				card.AddKeyword(CardKeyword.Unplayable);
			}
		}
		catch (Exception e)
		{
			Log.Warn(Sts2ForgeMod.LogPrefix + "keyword sync failed for " + Describe(card) + ": " + e.Message);
		}
	}

	/// <summary>
	/// The choice ID distinguishes retries at the same level without advancing the run RNG.
	/// </summary>
	public static Rng CreateRng(Player owner, CardModel main, int level, int materialCount, uint choiceId)
	{
		ulong mixin = ((ulong)(uint)owner.RunState.TotalFloor * 1000003UL
			+ (ulong)(uint)level * 1009UL
			+ (ulong)(uint)materialCount) * 31UL + 17UL + choiceId * 2654435761UL;
		return new Rng(owner, main.Id, mixin);
	}

	public static bool RollSuccess(Player owner, CardModel main, int level, int materialCount, int successPercent, uint choiceId)
	{
		int percent = Math.Clamp(successPercent, 0, 100);
		if (percent <= 0)
		{
			return false;
		}
		if (percent >= 100)
		{
			return true;
		}
		return CreateRng(owner, main, level, materialCount, choiceId).NextInt(100) < percent;
	}

	internal static int LevelAfterRound(int level, bool success) => Math.Clamp(level + (success ? 1 : -1), MinLevel, MaxLevel);

	/// <summary>
	/// Expected number of consumed same-name cards to reach targetLevel.
	/// F_n = ((n+1) + (1-p)F_(n-1)) / p.
	/// </summary>
	public static double ExpectedMaterials(int targetLevel, double successProbability)
	{
		if (targetLevel <= 0)
		{
			return 0.0;
		}
		double p = Math.Clamp(successProbability, 0.0, 1.0);
		if (p <= 0.0)
		{
			return double.PositiveInfinity;
		}
		double expected = 0.0;
		for (int level = 0; level < targetLevel; level++)
		{
			expected = ((level + 1) + (1.0 - p) * expected) / p;
		}
		return expected;
	}

	public static string FormatExpected(int targetLevel, double successProbability)
	{
		double value = ExpectedMaterials(targetLevel, successProbability);
		if (double.IsNaN(value) || double.IsInfinity(value))
		{
			return "∞";
		}
		return value.ToString("0.#", CultureInfo.InvariantCulture);
	}

	public static string GetLevelLabel()
	{
		return Loc("FORGE.CARD_LABEL", "强化");
	}

	public static string GetLevelText(int level)
	{
		return GetLevelLabel() + " " + level.ToString(CultureInfo.InvariantCulture);
	}

	public static string GetCardText(CardModel card)
	{
		return GetLevelText(GetLevel(card));
	}

	public static string BuildHoverTipBody(int level)
	{
		StringBuilder builder = new StringBuilder();
		AppendLine(builder, "FORGE.TIP_LEVEL", "强化等级：{0}（成功时 +1，上限 5）", level);
		if (level >= 1)
		{
			AppendLine(builder, "FORGE.TIP_DAMAGE", "攻击牌：伤害 +3");
			AppendLine(builder, "FORGE.TIP_BLOCK", "技能牌：格挡 +3");
		}
		if (level >= LevelDrawOnPower)
		{
			AppendLine(builder, "FORGE.TIP_DRAW", "能力牌打出时抽 1 张牌");
			AppendLine(builder, "FORGE.TIP_CURSE", "诅咒牌可以打出（0 费，进弃牌堆）");
		}
		if (level >= LevelEnergyOnPlay)
		{
			AppendLine(builder, "FORGE.TIP_ENERGY", "打出时获得 1 点能量");
		}
		if (level >= LevelExtraPlay)
		{
			AppendLine(builder, "FORGE.TIP_REPLAY", "打出时额外再打一次（与重放次数相乘）");
		}
		if (level >= LevelMiracleOnPlay)
		{
			AppendLine(builder, "FORGE.TIP_MIRACLE", "打出时获得 1 层奇迹（抵消下一次生命损失）");
		}
		return builder.ToString().TrimEnd('\n');
	}

	public static string Describe(CardModel? card)
	{
		if (card == null)
		{
			return "<none>";
		}
		return card.Id.Entry + "#" + GetLevel(card);
	}

	private static void AppendLine(StringBuilder builder, string key, string fallback)
	{
		builder.Append(Loc(key, fallback)).Append('\n');
	}

	private static void AppendLine(StringBuilder builder, string key, string fallback, int value)
	{
		string text = Loc(key, fallback);
		builder.Append(text.Replace("{0}", value.ToString(CultureInfo.InvariantCulture))).Append('\n');
	}

	public static string Loc(string key, string fallback)
	{
		try
		{
			LocString locString = new LocString(LocTable, key);
			string text = locString.GetFormattedText();
			if (string.IsNullOrEmpty(text) || string.Equals(text, key, StringComparison.Ordinal))
			{
				return fallback;
			}
			return text;
		}
		catch (Exception)
		{
			return fallback;
		}
	}
}
