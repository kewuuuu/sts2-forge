using Godot;
using HarmonyLib;
using Sts2Forge.Art;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using Sts2Forge.Combat;

namespace Sts2Forge.Patching;

internal static class MiraclePatches
{
	internal static bool Register(Harmony harmony)
	{
		void Patch(Type owner, string method, string handler, bool after = false)
		{
			var hook = new HarmonyMethod(typeof(MiraclePatches), handler);
			harmony.Patch(AccessTools.DeclaredMethod(owner, method), prefix: after ? null : hook, postfix: after ? hook : null);
		}
		Patch(typeof(CardEnergyCost), nameof(CardEnergyCost.GetWithModifiers), nameof(Cost), true);
		Patch(typeof(PlayerCombatState), nameof(PlayerCombatState.HasEnoughResourcesFor), nameof(RequireEnergy), true);
		Patch(typeof(CardModel), "SpendEnergy", nameof(SpendManualX));
		Patch(typeof(Creature), nameof(Creature.LoseHpInternal), nameof(ProtectAmount));
		Patch(typeof(Creature), nameof(Creature.SetCurrentHpInternal), nameof(ProtectTarget));
		Patch(typeof(Creature), nameof(Creature.SetMaxHpInternal), nameof(ProtectTarget));
		Patch(typeof(CreatureCmd), nameof(CreatureCmd.SetCurrentHp), nameof(ProtectCommand));
		Patch(typeof(CreatureCmd), "KillWithoutCheckingWinCondition", nameof(ProtectKill));
		Patch(typeof(PowerModel), "get_Icon", nameof(Icon));
		Patch(typeof(PowerModel), "get_BigIcon", nameof(Icon));
		Patch(typeof(PowerModel), "get_PackedIconPath", nameof(SmallIcon));
		Patch(typeof(PowerModel), "get_ResolvedBigIconPath", nameof(BigIcon));
		var autoPlay = AccessTools.DeclaredMethod(typeof(CardCmd), nameof(CardCmd.AutoPlay));
		harmony.Patch(autoPlay, prefix: new HarmonyMethod(typeof(MiraclePatches), nameof(EnterAuto)),
			finalizer: new HarmonyMethod(typeof(MiraclePatches), nameof(ExitAuto)));
		Patch(typeof(CardModel), nameof(CardModel.OnPlayWrapper), nameof(BeginPlay));
		return true;
	}

	private sealed record AutoScope(CardModel Card, bool Prepaid);
	private static readonly AsyncLocal<AutoScope?> Auto = new();
	private static readonly AsyncLocal<CardModel?> Resuming = new();

	// The async game method captures this scope before its first yield. Restore the caller's scope on return.
	private static void EnterAuto(CardModel card, bool skipXCapture, out AutoScope? __state)
	{
		__state = Auto.Value;
		Auto.Value = new(card, skipXCapture);
	}

	private static void ExitAuto(AutoScope? __state) => Auto.Value = __state;

	private static bool BeginPlay(CardModel __instance, PlayerChoiceContext choiceContext, Creature? target, bool isAutoPlay,
		ResourceInfo resources, bool skipCardPileVisuals, ref Task __result)
	{
		if (ReferenceEquals(Resuming.Value, __instance))
		{
			Resuming.Value = null;
			return true;
		}
		if (!isAutoPlay || Auto.Value is { Prepaid: true } scope && ReferenceEquals(scope.Card, __instance)
			|| !ForgeMiraclePower.Protects(__instance.Owner.Creature)) return true;
		__result = PlayAuto(__instance, choiceContext, target, true, resources, skipCardPileVisuals, false);
		return false;
	}

	private static async Task PlayAuto(CardModel card, PlayerChoiceContext context, Creature? target, bool auto, ResourceInfo resources, bool skipVisuals, bool prepaid)
	{
		var player = card.Owner;
		if (!prepaid && ForgeMiraclePower.Protects(player.Creature))
		{
			if (player.PlayerCombatState!.Energy < 1)
			{
				await CardPileCmd.Add(card, PileType.Play);
				await card.MoveToResultPileWithoutPlaying(context);
				return;
			}
			if (card.EnergyCost.CostsX)
				card.EnergyCost.CapturedXValue = Math.Max(0, card.EnergyCost.CapturedXValue - 1);
			var combat = player.Creature.CombatState!;
			CombatManager.Instance.History.EnergySpent(combat, 1, player);
			player.PlayerCombatState.LoseEnergy(1);
			await Hook.AfterEnergySpent(combat, card, 1);
			resources = resources with { EnergySpent = 1 };
		}
		var previous = Resuming.Value;
		try
		{
			Resuming.Value = card;
			await card.OnPlayWrapper(context, target, auto, resources, skipVisuals);
		}
		finally { Resuming.Value = previous; }
	}

	private static bool Active(CardModel card) => !card.IsCanonical && card.CombatState != null && card.Owner != null && ForgeMiraclePower.Protects(card.Owner.Creature);

	private static void Cost(CardModel ____card, CostModifiers modifiers, ref int __result)
	{
		if ((modifiers & CostModifiers.Global) != 0 && Active(____card))
			__result = Math.Max(0, __result) + 1;
	}

	private static void RequireEnergy(PlayerCombatState __instance, CardModel card, ref UnplayableReason reason, ref bool __result)
	{
		if (__instance.Energy < 1 && Active(card))
		{
			reason |= UnplayableReason.EnergyCostTooHigh;
			__result = false;
		}
	}

	private static bool SpendManualX(CardModel __instance, int amount, ref Task __result)
	{
		if (!__instance.EnergyCost.CostsX || !Active(__instance)) return true;
		__instance.EnergyCost.CapturedXValue = Math.Max(0, amount - 1);
		if (amount > 0)
		{
			CombatManager.Instance.History.EnergySpent(__instance.CombatState!, amount, __instance.Owner);
			__instance.Owner.PlayerCombatState!.LoseEnergy(amount);
		}
		__result = Hook.AfterEnergySpent(__instance.CombatState!, __instance, amount);
		return false;
	}

	private static void ProtectAmount(Creature __instance, ref decimal amount)
	{
		if (amount > 0 && ForgeMiraclePower.Protects(__instance)) amount = 0;
	}

	private static void ProtectTarget(Creature __instance, ref decimal amount) => ProtectCommand(__instance, ref amount);

	private static void ProtectCommand(Creature creature, ref decimal amount)
	{
		if (amount < creature.CurrentHp && ForgeMiraclePower.Protects(creature)) amount = creature.CurrentHp;
	}

	private static bool ProtectKill(Creature creature, ref Task __result)
	{
		if (!ForgeMiraclePower.Protects(creature)) return true;
		__result = Task.CompletedTask;
		return false;
	}

	private static bool Icon(PowerModel __instance, ref Texture2D __result)
	{
		if (__instance is not ForgeMiraclePower || ForgeArt.Get("Sts2Forge.Art.forge_miracle.png") is not { } texture) return true;
		__result = texture;
		return false;
	}

	private static bool SmallIcon(PowerModel __instance, ref string __result)
	{
		if (__instance is not ForgeMiraclePower) return true;
		__result = ModelDb.Power<BufferPower>().PackedIconPath;
		return false;
	}

	private static bool BigIcon(PowerModel __instance, ref string __result)
	{
		if (__instance is not ForgeMiraclePower) return true;
		__result = ModelDb.Power<BufferPower>().ResolvedBigIconPath;
		return false;
	}
}
