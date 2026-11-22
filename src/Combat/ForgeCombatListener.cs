using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using Sts2Forge.Core;

namespace Sts2Forge.Combat;

/// <summary>
/// Hook listener for the on-play parts of the forge table (+1 block, +2 draw, +3 energy, +5 miracle).
/// Registered once per combat state through ModHelper.SubscribeForCombatStateHooks.
/// </summary>
public sealed class ForgeCombatListener : AbstractModel
{
	private static ForgeCombatListener? _resolved;

	private static bool _warned;

	public override bool ShouldReceiveCombatHooks => true;

	/// <summary>
	/// Resolved lazily, never cached as a failure: mod init runs before ModelDb.Init (OneTimeInitialization.cs:59 vs
	/// :81), so the lookup is guaranteed to miss if it happens during init. Retry on every call until it succeeds.
	/// </summary>
	internal static ForgeCombatListener? TryGet()
	{
		if (_resolved != null)
		{
			return _resolved;
		}
		try
		{
			_resolved = ModelDb.GetByIdOrNull<ForgeCombatListener>(ModelDb.GetId<ForgeCombatListener>());
		}
		catch (Exception e)
		{
			if (!_warned)
			{
				_warned = true;
				Log.Warn(Sts2ForgeMod.LogPrefix + "combat listener lookup failed: " + e.Message);
			}
		}
		if (_resolved != null)
		{
			Log.Info(Sts2ForgeMod.LogPrefix + "combat listener resolved: " + _resolved.Id.Entry);
		}
		return _resolved;
	}

	internal static IEnumerable<AbstractModel> ProvideForCombat(CombatState combatState)
	{
		ForgeCombatListener? listener = TryGet();
		if (listener == null)
		{
			return Array.Empty<AbstractModel>();
		}
		return new AbstractModel[] { listener };
	}

	public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		try
		{
			if (cardPlay?.Card == null || cardPlay.Player == null)
			{
				return;
			}
			CardModel card = cardPlay.Card;
			int level = ForgeLevels.GetLevel(card);
			if (level <= 0)
			{
				return;
			}
			// Same Move block as Adroit. A separate source keeps the +2 skill bonus on the native block.
			await CreatureCmd.GainBlock(cardPlay.Player.Creature, 3m, ValueProp.Move, null);
			if (level >= ForgeLevels.LevelDrawOnPower && card.Type == CardType.Power)
			{
				await CardPileCmd.Draw(choiceContext, ForgeLevels.CardsDrawnOnPower, cardPlay.Player);
			}
			if (level >= ForgeLevels.LevelEnergyOnPlay)
			{
				await PlayerCmd.GainEnergy(ForgeLevels.EnergyPerPlay, cardPlay.Player);
			}
			if (level >= ForgeLevels.LevelMiracleOnPlay)
			{
				await PowerCmd.Apply<ForgeMiraclePower>(choiceContext, cardPlay.Player.Creature, ForgeLevels.MiraclePerPlay, cardPlay.Player.Creature, card, true);
			}
		}
		catch (Exception e)
		{
			Log.Warn(Sts2ForgeMod.LogPrefix + "forged card on-play effect failed: " + e.Message);
		}
	}
}
