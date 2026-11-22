using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Runs;
using Sts2Forge.Config;
using Sts2Forge.Core;
using Sts2Forge.UI;

namespace Sts2Forge.Entities;

public sealed class ForgeRestSiteOption(Player owner) : RestSiteOption(owner)
{
	public const string ForgeOptionId = "FORGE";
	public override string OptionId => ForgeOptionId;
	public override IEnumerable<string> AssetPaths => NCardSmithVfx.AssetPaths;
	public override bool IsEnabled => !IsSpent(ForgeRuntimeConfig.ForThisRun, Owner.RunState.TotalFloor)
		&& Owner.Deck.Cards.Any(ForgeLevels.IsForgeable);

	private bool IsSpent(ForgeConfig config, int floor) => config.OneForgePerRest && ForgeAttempts.HasForged(Owner.NetId, floor);

	public override async Task<bool> OnSelect()
	{
		ForgeConfig config = ForgeRuntimeConfig.ForThisRun;
		int floor = Owner.RunState.TotalFloor;
		if (IsSpent(config, floor))
		{
			return false;
		}

		ForgeScreen? screen = null;
		int rounds = 0;
		RunManager runManager = RunManager.Instance;
		bool local = LocalContext.IsMe(Owner) && runManager.NetService.Type != NetGameType.Replay;
		var sync = runManager.PlayerChoiceSynchronizer;
		try
		{
			CardModel[] targets = Owner.Deck.Cards.Where(ForgeLevels.IsForgeable).ToArray();
			uint targetChoice = sync.ReserveChoiceId(Owner);
			List<int> targetSelection;
			if (local)
			{
				try
				{
					screen = ForgeScreen.TryOpen(config.SuccessPercent, Owner.Deck.Cards);
					targetSelection = screen == null ? [] : await screen.ChooseTarget(targets);
				}
				catch (Exception e)
				{
					Log.Warn(Sts2ForgeMod.LogPrefix + "forge target selection failed: " + e);
					targetSelection = [];
				}
				sync.SyncLocalChoice(Owner, targetChoice, PlayerChoiceResult.FromIndexes(targetSelection));
			}
			else
			{
				targetSelection = (await sync.WaitForRemoteChoice(Owner, targetChoice)).AsIndexes();
			}
			if (targetSelection.Count == 0)
			{
				return false;
			}
			if (targetSelection.Count != 1 || (uint)targetSelection[0] >= (uint)targets.Length)
			{
				throw new InvalidOperationException("Invalid forge target choice.");
			}

			CardModel target = targets[targetSelection[0]];
			while (true)
			{
				int level = ForgeLevels.GetLevel(target);
				int required = level + 1;
				CardModel[] candidates = Owner.Deck.Cards.Where(card => !ReferenceEquals(card, target)
					&& card.Id.Equals(target.Id) && card.IsRemovable).ToArray();
				bool canForge = ForgeLevels.IsForgeable(target) && Owner.Deck.Cards.Any(card => ReferenceEquals(card, target))
					&& candidates.Length >= required;
				uint roundChoice = sync.ReserveChoiceId(Owner);
				List<int> roundSelection;
				if (local)
				{
					try
					{
						roundSelection = screen == null ? [] : await screen.ChooseMaterials(target, candidates, required, canForge);
					}
					catch (Exception e)
					{
						Log.Warn(Sts2ForgeMod.LogPrefix + "forge material selection failed: " + e);
						roundSelection = [];
					}
					if (roundSelection.Count > 0)
					{
						bool success = ForgeLevels.RollSuccess(Owner, target, level, required, config.SuccessPercent, roundChoice);
						roundSelection.Add(success ? 1 : 0);
					}
					// Empty means finish. Otherwise the last value is the owner's roll result.
					sync.SyncLocalChoice(Owner, roundChoice, PlayerChoiceResult.FromIndexes(roundSelection));
				}
				else
				{
					roundSelection = (await sync.WaitForRemoteChoice(Owner, roundChoice)).AsIndexes();
				}
				if (roundSelection.Count == 0)
				{
					break;
				}

				int outcome = roundSelection[^1];
				List<int> indexes = roundSelection.GetRange(0, roundSelection.Count - 1);
				if (!canForge || outcome is not (0 or 1) || indexes.Count != required
					|| indexes.Distinct().Count() != required || indexes.Any(i => (uint)i >= (uint)candidates.Length))
				{
					throw new InvalidOperationException("Invalid forge round choice.");
				}
				CardModel[] materials = indexes.Order().Select(i => candidates[i]).ToArray();
				if (materials.Any(card => !card.IsRemovable || !Owner.Deck.Cards.Any(inDeck => ReferenceEquals(inDeck, card))))
				{
					throw new InvalidOperationException("Forge material is no longer removable from this deck.");
				}
				await CardPileCmd.RemoveFromDeck(materials, showPreview: false);
				bool succeeded = outcome == 1;
				int nextLevel = ForgeLevels.LevelAfterRound(level, succeeded);
				ForgeLevels.SetLevel(target, nextLevel);
				rounds++;
				Log.Info(Sts2ForgeMod.LogPrefix + $"forge {(succeeded ? "success" : "fail")}: card={target.Id.Entry} level {level} -> {nextLevel} p={config.SuccessPercent}% materials={materials.Length} floor={floor} round={rounds}");
				if (config.LogDebug)
				{
					Log.Debug(Sts2ForgeMod.LogPrefix + $"forge detail: owner={Owner.NetId} seed_level={level} choice_id={roundChoice}");
				}
				if (screen != null)
				{
					await screen.ShowRoundResult(rounds, level, nextLevel, materials.Length, succeeded);
				}
			}
		}
		catch (Exception e)
		{
			Log.Error(Sts2ForgeMod.LogPrefix + "forge option failed: " + e);
		}
		finally
		{
			try
			{
				screen?.Close();
			}
			catch (Exception e)
			{
				Log.Warn(Sts2ForgeMod.LogPrefix + "forge screen cleanup failed: " + e);
			}
		}

		if (rounds == 0)
		{
			return false;
		}
		int sessions = ForgeAttempts.Register(Owner.NetId, floor);
		if (local && !config.ConsumeRestAction && NRestSiteRoom.Instance?.GetButtonForOption(this) is { } button)
		{
			try
			{
				// Reload updates the appearance; vanilla only initializes the click guard when creating the button.
				AccessTools.Field(typeof(NRestSiteButton), "_isUnclickable")?.SetValue(button, !IsEnabled);
				button.Option = this;
			}
			catch (Exception e)
			{
				Log.Warn(Sts2ForgeMod.LogPrefix + "forge button refresh failed: " + e);
			}
		}
		Log.Info(Sts2ForgeMod.LogPrefix + $"forge session ended: floor={floor} rounds={rounds} session={sessions} consume_rest_action={Sts2ForgeMod.BoolText(config.ConsumeRestAction)}");
		return config.ConsumeRestAction;
	}
}
