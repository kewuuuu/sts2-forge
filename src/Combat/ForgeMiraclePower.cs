using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace Sts2Forge.Combat;

public sealed class ForgeMiraclePower : PowerModel
{
	public override PowerType Type => PowerType.Buff;
	public override PowerStackType StackType => PowerStackType.Counter;

	internal static bool Protects(Creature creature) => creature.IsPlayer && creature.GetPowerAmount<ForgeMiraclePower>() > 0;

	public override decimal ModifyHpLostAfterOsty(Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
		=> target == Owner && Amount > 0 ? 0m : amount;

	public override decimal ModifyMaxEnergy(Player player, decimal amount)
		=> player.Creature == Owner ? amount + Amount : amount;

	public override async Task BeforeSideTurnEndVeryEarly(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
	{
		if (side == CombatSide.Player && participants.Contains(Owner) && Owner.Player?.PlayerCombatState is { } state)
		{
			int loss = Math.Min(Amount, state.Hand.Cards.Count + 1);
			await PowerCmd.ModifyAmount(choiceContext, this, -loss, null, null);
		}
	}
}
