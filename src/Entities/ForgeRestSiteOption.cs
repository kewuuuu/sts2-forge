using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using Sts2Forge.Config;
using Sts2Forge.Core;
using Sts2Forge.UI;

namespace Sts2Forge.Entities;

/// <summary>
/// One forge attempt: pick a card, feed it (level+1) same-name copies, optionally protect the level
/// with a pad card of the same forge level, then roll once.
/// </summary>
public sealed class ForgeRestSiteOption : RestSiteOption
{
	public const string ForgeOptionId = "FORGE";

	public ForgeRestSiteOption(Player owner)
		: base(owner)
	{
	}

	public override string OptionId => ForgeOptionId;

	public override bool IsEnabled
	{
		get
		{
			try
			{
				if (ForgeRuntimeConfig.ForThisRun.OneForgePerRest && ForgeAttempts.HasForged(base.Owner.NetId, base.Owner.RunState.TotalFloor))
				{
					// The attempt budget for this rest is spent: leave the option greyed out instead of
					// letting the player click a button that only logs a refusal.
					return false;
				}
				return base.Owner.Deck.Cards.Any(card => ForgeLevels.IsForgeable(card));
			}
			catch (Exception)
			{
				// A deck that cannot be read must not advertise a forge the flow would fail to start.
				return false;
			}
		}
	}

	public override async Task<bool> OnSelect()
	{
		try
		{
			return await ForgeAsync();
		}
		catch (Exception e)
		{
			Log.Error(Sts2ForgeMod.LogPrefix + "forge option failed: " + e);
			return false;
		}
	}

	private async Task<bool> ForgeAsync()
	{
		Player owner = base.Owner;
		ForgeConfig config = ForgeRuntimeConfig.ForThisRun;
		int floor = owner.RunState.TotalFloor;
		if (config.OneForgePerRest && ForgeAttempts.HasForged(owner.NetId, floor))
		{
			Log.Info(Sts2ForgeMod.LogPrefix + "already forged at floor " + floor + "; refusing a second attempt");
			return false;
		}
		LocString mainPrompt = new LocString(ForgeLevels.LocTable, config.SuccessPercent <= 0 ? "FORGE.SELECT_MAIN_P0" : "FORGE.SELECT_MAIN");
		mainPrompt.Add("percent", config.SuccessPercent);
		CardModel? main = await Select(owner, ForgeLevels.IsForgeable, mainPrompt, 1);
		if (main == null)
		{
			Log.Info(Sts2ForgeMod.LogPrefix + "forge cancelled: no card selected");
			return false;
		}
		int level = ForgeLevels.GetLevel(main);
		// A pad only absorbs the level loss, and a level-0 card cannot lose a level: asking for one
		// there would burn a card for nothing. Skip the step instead of offering a pointless trade.
		CardModel? pad = null;
		if (level > ForgeLevels.MinLevel)
		{
			LocString padPrompt = new LocString(ForgeLevels.LocTable, "FORGE.SELECT_PAD");
			pad = await Select(owner, card => !ReferenceEquals(card, main) && ForgeLevels.GetLevel(card) == level, padPrompt, 0);
		}
		if (pad != null && ReferenceEquals(pad, main))
		{
			pad = null;
		}
		int required = level + 1;
		List<CardModel> materials = PickMaterials(owner, main, pad, required, out int available);
		if (materials.Count < required)
		{
			Log.Info(Sts2ForgeMod.LogPrefix + "forge refused: need " + required + " copies of " + main.Id.Entry + ", deck has " + available);
			await NotifyNotEnough(owner, main, required, available);
			return false;
		}
		string padName = pad != null ? CardName(pad) : "none";
		bool success = ForgeLevels.RollSuccess(owner, main, level, materials.Count, config.SuccessPercent);
		await CardPileCmd.RemoveFromDeck(materials, true);
		int newLevel = level;
		if (success)
		{
			newLevel = Math.Min(ForgeLevels.MaxLevel, level + 1);
			ForgeLevels.SetLevel(main, newLevel);
		}
		else if (pad != null)
		{
			await CardPileCmd.RemoveFromDeck(pad, true);
		}
		else
		{
			newLevel = Math.Max(ForgeLevels.MinLevel, level - 1);
			ForgeLevels.SetLevel(main, newLevel);
		}
		int attempts = ForgeAttempts.Register(owner.NetId, floor);
		string outcome = success ? "success" : (pad != null ? "fail (pad consumed)" : "fail");
		Log.Info(Sts2ForgeMod.LogPrefix + "forge " + outcome + ": card=" + main.Id.Entry
			+ " level " + level + " -> " + newLevel
			+ " p=" + config.SuccessPercent + "%"
			+ " materials=" + materials.Count
			+ " pad=" + padName
			+ " floor=" + floor
			+ " attempt=" + attempts);
		if (config.LogDebug)
		{
			Log.Debug(Sts2ForgeMod.LogPrefix + "forge detail: owner=" + owner.NetId
				+ " seed_level=" + level
				+ " total_floor=" + floor
				+ " consume_rest_action=" + Sts2ForgeMod.BoolText(config.ConsumeRestAction)
				+ " one_forge_per_rest=" + Sts2ForgeMod.BoolText(config.OneForgePerRest));
		}
		await NotifyResult(owner, main, level, newLevel, success, pad, materials.Count);
		return config.ConsumeRestAction;
	}

	/// <summary>
	/// minCount 0 turns the confirm button into a real skip: with MinSelect equal to MaxSelect the
	/// vanilla deck screen keeps that button disabled (NDeckCardSelectScreen.RefreshConfirmButtonVisibility),
	/// which leaves the back arrow as the only way out.
	/// </summary>
	private static async Task<CardModel?> Select(Player owner, Func<CardModel, bool> filter, LocString prompt, int minCount)
	{
		CardSelectorPrefs prefs = new CardSelectorPrefs(prompt, minCount, 1)
		{
			Cancelable = true,
			RequireManualConfirmation = true
		};
		IEnumerable<CardModel> selected = await CardSelectCmd.FromDeckGeneric(owner, prefs, filter);
		return selected.FirstOrDefault();
	}

	private static List<CardModel> PickMaterials(Player owner, CardModel main, CardModel? pad, int required, out int available)
	{
		List<CardModel> materials = new List<CardModel>(required);
		int count = 0;
		foreach (CardModel card in owner.Deck.Cards)
		{
			if (ReferenceEquals(card, main))
			{
				continue;
			}
			if (pad != null && ReferenceEquals(card, pad))
			{
				continue;
			}
			if (!card.Id.Equals(main.Id))
			{
				continue;
			}
			count++;
			if (materials.Count < required)
			{
				materials.Add(card);
			}
		}
		available = count;
		return materials;
	}

	private static async Task NotifyNotEnough(Player owner, CardModel main, int required, int available)
	{
		LocString body = new LocString(ForgeLevels.LocTable, "FORGE.NOT_ENOUGH_BODY");
		body.Add("card", CardName(main));
		body.Add("need", required);
		body.Add("have", available);
		await Tell(owner, new LocString(ForgeLevels.LocTable, "FORGE.NOT_ENOUGH"), body);
	}

	private static async Task NotifyResult(Player owner, CardModel main, int level, int newLevel, bool success, CardModel? pad, int used)
	{
		string titleKey = success ? "FORGE.SUCCESS" : (pad != null ? "FORGE.FAIL_PADDED" : "FORGE.FAIL");
		string bodyKey = success ? "FORGE.RESULT_SUCCESS_BODY" : (pad != null ? "FORGE.RESULT_FAIL_PAD_BODY" : "FORGE.RESULT_FAIL_BODY");
		LocString body = new LocString(ForgeLevels.LocTable, bodyKey);
		body.Add("card", CardName(main));
		body.Add("from", level);
		body.Add("to", newLevel);
		body.Add("used", used);
		body.Add("pad", pad != null ? CardName(pad) : string.Empty);
		await Tell(owner, new LocString(ForgeLevels.LocTable, titleKey), body);
	}

	/// <summary>
	/// Only the player who forged gets a modal: the same flow replays on every peer, and a dialog on
	/// each of them would be an unclickable duplicate.
	/// </summary>
	private static async Task Tell(Player owner, LocString header, LocString body)
	{
		if (!LocalContext.IsMe(owner))
		{
			return;
		}
		bool? shown = await ForgePopup.Tell(body, header);
		if (!shown.HasValue)
		{
			Log.Info(Sts2ForgeMod.LogPrefix + "forge popup not shown; the log line above carries the outcome");
		}
	}

	private static string CardName(CardModel card)
	{
		try
		{
			string text = card.TitleLocString.GetFormattedText();
			if (!string.IsNullOrEmpty(text))
			{
				return text;
			}
		}
		catch (Exception)
		{
		}
		return card.Id.Entry;
	}
}
