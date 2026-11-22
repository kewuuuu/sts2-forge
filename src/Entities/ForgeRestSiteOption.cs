using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using Sts2Forge.Config;
using Sts2Forge.Core;

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
				return base.Owner.Deck.Cards.Any(card => ForgeLevels.IsForgeable(card));
			}
			catch (Exception)
			{
				return true;
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
		string mainPrompt = config.SuccessPercent <= 0 ? "FORGE.SELECT_MAIN_P0" : "FORGE.SELECT_MAIN";
		CardModel? main = await Select(owner, ForgeLevels.IsForgeable, mainPrompt);
		if (main == null)
		{
			Log.Info(Sts2ForgeMod.LogPrefix + "forge cancelled: no card selected");
			return false;
		}
		int level = ForgeLevels.GetLevel(main);
		CardModel? pad = await Select(owner, card => !ReferenceEquals(card, main) && ForgeLevels.GetLevel(card) == level, "FORGE.SELECT_PAD");
		if (pad != null && ReferenceEquals(pad, main))
		{
			pad = null;
		}
		int required = level + 1;
		List<CardModel> materials = PickMaterials(owner, main, pad, required, out int available);
		if (materials.Count < required)
		{
			Log.Info(Sts2ForgeMod.LogPrefix + "forge refused: need " + required + " copies of " + main.Id.Entry + ", deck has " + available);
			return false;
		}
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
			+ " pad=" + (pad != null ? pad.Id.Entry : "none")
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
		return config.ConsumeRestAction;
	}

	private static async Task<CardModel?> Select(Player owner, Func<CardModel, bool> filter, string promptKey)
	{
		CardSelectorPrefs prefs = new CardSelectorPrefs(new LocString(ForgeLevels.LocTable, promptKey), 1)
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
}
