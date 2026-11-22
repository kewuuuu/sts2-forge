using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Localization.Fonts;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using Sts2Forge.Core;
using Sts2Forge.Localization;

namespace Sts2Forge.UI;

internal sealed partial class ForgeScreen : Control, IOverlayScreen
{
	private static readonly string[] ReturnHotkeys = [MegaInput.cancel, MegaInput.pauseAndBack, MegaInput.back];
	private static readonly Action IgnoreRelease = () => { };

	private readonly Label _status = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
	private readonly VBoxContainer _choices = new();
	private readonly VBoxContainer _history = new();
	private readonly Control _cardSlot = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
	private readonly Button _confirm = new() { Disabled = true };
	private readonly Button _finish = new() { Disabled = true };
	private TaskCompletionSource<List<int>>? _choice;
	private NOverlayStack? _stack;
	private NHotkeyManager? _hotkeys;
	private NCard? _card;
	private bool _ended;
	private Action? _commit;

	public NetScreenType ScreenType => NetScreenType.CardSelection;
	public bool UseSharedBackstop => false;
	public Control DefaultFocusedControl => _choices.GetChildren().OfType<Button>().FirstOrDefault(b => !b.Disabled) ?? _finish;

	internal static ForgeScreen? TryOpen(int percent)
	{
		NOverlayStack? stack = NOverlayStack.Instance;
		if (stack == null)
		{
			return null;
		}
		ForgeScreen? screen = null;
		try
		{
			screen = new ForgeScreen { _stack = stack };
			screen.Build(percent);
			stack.Push(screen);
			return screen;
		}
		catch (Exception e)
		{
			Log.Warn(Sts2ForgeMod.LogPrefix + "forge screen unavailable: " + e);
			screen?.Close();
			return null;
		}
	}

	private void Build(int percent)
	{
		TreeExiting += OnTreeExiting;
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		ColorRect background = new() { Color = new Color("211921") };
		AddChild(background);
		background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

		MarginContainer margins = new();
		foreach (string side in new[] { "left", "right", "top", "bottom" })
		{
			margins.AddThemeConstantOverride("margin_" + side, 44);
		}
		AddChild(margins);
		margins.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		VBoxContainer page = new();
		page.AddThemeConstantOverride("separation", 18);
		margins.AddChild(page);
		page.AddChild(Heading(T("TITLE", "Forge"), 42));
		_status.ApplyLocaleFontSubstitution(FontType.Regular, "font");
		_status.AddThemeFontSizeOverride("font_size", 24);
		page.AddChild(_status);
		page.AddChild(Heading(string.Format(T("PROBABILITY", "Success chance: {0}% · Materials are never refunded"), percent), 20));

		HBoxContainer columns = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
		columns.AddThemeConstantOverride("separation", 24);
		page.AddChild(columns);
		columns.AddChild(Scroll(_choices, 420));
		columns.AddChild(_cardSlot);
		columns.AddChild(Scroll(_history, 360));
		_history.AddChild(Heading(T("HISTORY", "Rounds"), 27));

		HBoxContainer actions = new() { Alignment = BoxContainer.AlignmentMode.End };
		actions.AddThemeConstantOverride("separation", 16);
		page.AddChild(actions);
		foreach (Button button in new[] { _finish, _confirm })
		{
			button.CustomMinimumSize = new Vector2(200, 48);
			button.ApplyLocaleFontSubstitution(FontType.Regular, "font");
			button.AddThemeFontSizeOverride("font_size", 24);
			actions.AddChild(button);
		}
		_finish.Text = T("FINISH", "End forging");
		_finish.Pressed += RequestFinish;
		_confirm.Pressed += () => _commit?.Invoke();
	}

	internal Task<List<int>> ChooseTarget(CardModel[] targets) => PresentChoice(() =>
	{
		_status.Text = T("TARGET", "Choose one target. It stays fixed until you finish forging.");
		_confirm.Hide();
		for (int i = 0; i < targets.Length; i++)
		{
			int index = i;
			Button button = new() { Text = CardLabel(targets[i], i), TooltipText = CardTooltip(targets[i]), Alignment = HorizontalAlignment.Left };
			button.Pressed += () => Complete([index]);
			AddChoice(button);
		}
	});

	internal Task<List<int>> ChooseMaterials(CardModel target, CardModel[] candidates, int required, bool canForge) => PresentChoice(() =>
	{
		if (_card == null)
		{
			_card = NCard.Create(target);
			if (_card != null)
			{
				_cardSlot.AddChild(_card);
				_card.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
				_card.MouseBehaviorRecursive = MouseBehaviorRecursiveEnum.Disabled;
				_card.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
			}
		}
		List<int> chosen = [];
		_confirm.Text = T("COMMIT", "Forge this round");
		_confirm.Show();
		_commit = () =>
		{
			if (canForge && chosen.Count == required)
			{
				Complete(chosen.ToList());
			}
		};
		List<CheckBox> boxes = [];
		for (int i = 0; i < candidates.Length; i++)
		{
			int index = i;
			CheckBox box = new() { Text = CardLabel(candidates[i], i), TooltipText = CardTooltip(candidates[i]), Disabled = !canForge };
			box.Toggled += selected =>
			{
				if (selected)
				{
					chosen.Add(index);
				}
				else
				{
					chosen.Remove(index);
				}
				UpdateSelection();
			};
			boxes.Add(box);
			AddChoice(box);
		}
		UpdateSelection();

		void UpdateSelection()
		{
			string detail = ForgeLevels.GetLevel(target) >= ForgeLevels.MaxLevel
				? T("MAX", "This card is at maximum forge level.")
				: canForge
					? string.Format(T("MATERIALS", "Select exactly {0} matching cards ({1}/{0} selected)."), required, chosen.Count)
					: string.Format(T("SHORTAGE", "Need {0} matching cards. Only {1} can be used."), required, candidates.Length);
			_status.Text = target.Title + " · " + ForgeLevels.GetLevelText(ForgeLevels.GetLevel(target)) + "\n" + detail;
			_confirm.Disabled = !canForge || chosen.Count != required || _choice == null;
			foreach (CheckBox box in boxes)
			{
				box.Disabled = !canForge || _choice == null || (chosen.Count >= required && !box.ButtonPressed);
			}
		}
	});

	internal async Task ShowRoundResult(int round, int from, int to, int used, bool success)
	{
		if (_ended)
		{
			return;
		}
		try
		{
			Label line = Heading(string.Format(T(success ? "ROUND_SUCCESS" : "ROUND_FAIL",
				success ? "Round {0} · Success · {1} → {2} · Used {3}" : "Round {0} · Failed · {1} → {2} · Used {3}"), round, from, to, used), 21);
			line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			_history.AddChild(line);
			_history.MoveChild(line, 1);
			_status.Text = line.Text;
			_card?.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
			if (_card != null && NCardSmithVfx.Create(_card) is { } vfx)
			{
				vfx.ZIndex = 1;
				_cardSlot.AddChild(vfx);
				SceneTreeTimer timer = GetTree().CreateTimer(0.95);
				await ToSignal(timer, SceneTreeTimer.SignalName.Timeout);
			}
		}
		catch (Exception e)
		{
			Log.Warn(Sts2ForgeMod.LogPrefix + "forge result UI failed: " + e);
		}
	}

	private Task<List<int>> PresentChoice(Action render)
	{
		if (_ended)
		{
			return Task.FromResult(new List<int>());
		}
		var choice = new TaskCompletionSource<List<int>>();
		_choice = choice;
		try
		{
			foreach (Node child in _choices.GetChildren())
			{
				_choices.RemoveChild(child);
				child.QueueFree();
			}
			_finish.Disabled = false;
			render();
			if (IsVisibleInTree())
			{
				DefaultFocusedControl.GrabFocus();
			}
		}
		catch (Exception e)
		{
			Log.Warn(Sts2ForgeMod.LogPrefix + "forge choice UI failed: " + e);
			End();
		}
		return choice.Task;
	}

	private void Complete(List<int> indexes)
	{
		if (_choice is not { } pending)
		{
			return;
		}
		_choice = null;
		_commit = null;
		_confirm.Disabled = true;
		_finish.Disabled = true;
		foreach (Button button in _choices.GetChildren().OfType<Button>())
		{
			button.Disabled = true;
		}
		Callable.From(() => pending.TrySetResult(indexes)).CallDeferred();
	}

	private void RequestFinish()
	{
		if (!_finish.Disabled)
		{
			End();
		}
	}

	private void End()
	{
		_ended = true;
		Complete([]);
	}

	private void HandleReturnHotkey()
	{
		if (_stack?.Peek() == this && IsVisibleInTree())
		{
			RequestFinish();
		}
	}

	private void UnbindReturnHotkeys()
	{
		if (GodotObject.IsInstanceValid(_hotkeys))
		{
			foreach (string hotkey in ReturnHotkeys)
			{
				_hotkeys!.RemoveHotkeyPressedBinding(hotkey, HandleReturnHotkey);
				_hotkeys.RemoveHotkeyReleasedBinding(hotkey, IgnoreRelease);
			}
		}
		_hotkeys = null;
	}

	internal void Close()
	{
		End();
		if (_stack != null && IsInsideTree())
		{
			_stack.Remove(this);
		}
		else if (!IsQueuedForDeletion())
		{
			QueueFree();
		}
	}

	public void AfterOverlayOpened() { }
	public void AfterOverlayShown()
	{
		Show();
		if (_hotkeys == null && NHotkeyManager.Instance is { } manager)
		{
			_hotkeys = manager;
			foreach (string hotkey in ReturnHotkeys)
			{
				manager.PushHotkeyPressedBinding(hotkey, HandleReturnHotkey);
				manager.PushHotkeyReleasedBinding(hotkey, IgnoreRelease);
			}
		}
	}

	public void AfterOverlayHidden()
	{
		UnbindReturnHotkeys();
		Hide();
	}
	public void AfterOverlayClosed()
	{
		UnbindReturnHotkeys();
		_stack = null;
		End();
		Hide();
		QueueFree();
	}

	private void OnTreeExiting()
	{
		UnbindReturnHotkeys();
		End();
	}

	private void AddChoice(Button button)
	{
		button.CustomMinimumSize = new Vector2(0, 40);
		button.ApplyLocaleFontSubstitution(FontType.Regular, "font");
		button.AddThemeFontSizeOverride("font_size", 22);
		_choices.AddChild(button);
	}

	private static ScrollContainer Scroll(VBoxContainer content, float minWidth)
	{
		ScrollContainer scroll = new() { CustomMinimumSize = new Vector2(minWidth, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
		content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroll.AddChild(content);
		return scroll;
	}

	private static Label Heading(string text, int size)
	{
		Label label = new() { Text = text };
		label.ApplyLocaleFontSubstitution(FontType.Regular, "font");
		label.AddThemeFontSizeOverride("font_size", size);
		return label;
	}

	private static string CardTooltip(CardModel card) => card.GetDescriptionForPile(PileType.None);

	private static string CardLabel(CardModel card, int index) => $"{card.Title} · {ForgeLevels.GetLevelText(ForgeLevels.GetLevel(card))}  #{index + 1}";

	private static string T(string key, string fallback) => ForgeLoc.Text("FORGE." + key, fallback);
}
