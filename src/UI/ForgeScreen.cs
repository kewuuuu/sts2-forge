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
using Sts2Forge.Art;
using Sts2Forge.Core;
using Sts2Forge.Localization;

namespace Sts2Forge.UI;

internal sealed partial class ForgeScreen : Control, IOverlayScreen
{
	private static readonly string[] ReturnHotkeys = [MegaInput.cancel, MegaInput.pauseAndBack, MegaInput.back];
	private static readonly Action IgnoreRelease = () => { };

	private readonly Label _status = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
	private readonly Label _choiceTitle = Heading("", 27);
	private readonly Label _targetIdentity = Heading("", 21);
	private readonly Label _level = Heading("", 24);
	private readonly Label _idle = Heading("", 23);
	private readonly HBoxContainer _levelTrack = new();
	private readonly Dictionary<CardModel, int> _deckSlots = new(ReferenceEqualityComparer.Instance);
	private readonly Dictionary<CardModel, Button> _rows = new(ReferenceEqualityComparer.Instance);
	private ScrollContainer _choiceScroll = null!;
	private CardModel? _focusedCard;
	private int _savedScroll;
	private bool _materials;
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

	internal static ForgeScreen? TryOpen(int percent, IReadOnlyList<CardModel> deck)
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
			for (int i = 0; i < deck.Count; i++)
			{
				screen._deckSlots.Add(deck[i], i + 1);
			}
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
		Resized += QueueRedraw;
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MarginContainer margins = new();
		foreach (string side in new[] { "left", "right", "top", "bottom" })
		{
			margins.AddThemeConstantOverride("margin_" + side, 34);
		}
		AddChild(margins);
		margins.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		VBoxContainer page = Column();
		margins.AddChild(page);
		HBoxContainer header = new();
		header.AddThemeConstantOverride("separation", 16);
		header.AddChild(new TextureRect
		{
			Texture = ForgeArt.RestSiteIcon,
			CustomMinimumSize = new Vector2(64, 64),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
		});
		Label title = Heading(T("TITLE", "Card Upgrade"), 42);
		title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		header.AddChild(title);
		header.AddChild(Heading(string.Format(T("PROBABILITY", "Success chance: {0}% · Materials are never refunded"), percent), 22));
		page.AddChild(Frame(header, Iron, Patina));

		HBoxContainer columns = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
		columns.AddThemeConstantOverride("separation", 14);
		page.AddChild(columns);
		VBoxContainer choices = Column();
		choices.AddChild(_choiceTitle);
		_status.ApplyLocaleFontSubstitution(FontType.Regular, "font");
		_status.AddThemeFontSizeOverride("font_size", 22);
		_status.AddThemeColorOverride("font_color", Muted);
		choices.AddChild(_status);
		_choices.AddThemeConstantOverride("separation", 7);
		_choiceScroll = Scroll(_choices);
		choices.AddChild(_choiceScroll);
		AddColumn(choices, 316, 1.1f);

		VBoxContainer center = Column();
		center.AddChild(Heading(T("LOCKED_TARGET", "Target card"), 27));
		_targetIdentity.Hide();
		center.AddChild(_targetIdentity);
		_cardSlot.CustomMinimumSize = new Vector2(248, 160);
		PanelContainer stage = Frame(_cardSlot, Soot, Patina, 5);
		stage.SizeFlagsVertical = SizeFlags.ExpandFill;
		center.AddChild(stage);
		_stageLight.Texture = Glow(new Color("613623"), Soot, new Vector2(0.5f, 0.6f));
		_cardSlot.AddChild(_stageLight);
		_stageLight.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_idle.Text = T("EMPTY_TARGET", "Select a target to begin.");
		_idle.HorizontalAlignment = HorizontalAlignment.Center;
		_cardSlot.AddChild(_idle);
		_idle.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_cardSlot.Resized += FitCard;
		_levelTrack.AddThemeConstantOverride("separation", 5);
		center.AddChild(_levelTrack);
		_level.HorizontalAlignment = HorizontalAlignment.Center;
		center.AddChild(_level);
		AddColumn(center, 280, 1.65f);

		VBoxContainer log = Column();
		log.AddChild(Heading(T("HISTORY", "This session"), 27));
		_history.AddThemeConstantOverride("separation", 8);
		Label emptyHistory = Heading(T("EMPTY_HISTORY", "No upgrade attempts yet."), 21);
		emptyHistory.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_history.AddChild(emptyHistory);
		log.AddChild(Scroll(_history));
		AddColumn(log, 254, 1f);

		HBoxContainer actions = new() { Alignment = BoxContainer.AlignmentMode.End };
		actions.AddThemeConstantOverride("separation", 14);
		page.AddChild(Frame(actions, Iron, Patina, 10));
		foreach (Button button in new[] { _finish, _confirm })
		{
			button.CustomMinimumSize = new Vector2(208, 48);
			StyleButton(button, button == _confirm, 23);
			actions.AddChild(button);
		}
		_finish.Text = T("FINISH", "Finish");
		_finish.Pressed += RequestFinish;
		_confirm.Text = T("COMMIT", "Upgrade");
		_confirm.Pressed += () => _commit?.Invoke();

		void AddColumn(Control content, float min, float ratio)
		{
			PanelContainer frame = Frame(content, Iron, Patina);
			frame.CustomMinimumSize = new Vector2(min, 0);
			frame.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			frame.SizeFlagsStretchRatio = ratio;
			columns.AddChild(frame);
		}
	}

	private void FitCard()
	{
		if (_card is { } card)
		{
			Vector2 available = _cardSlot.Size - new Vector2(32, 28);
			float scale = Mathf.Clamp(Mathf.Min(available.X / NCard.defaultSize.X, available.Y / NCard.defaultSize.Y), 0.1f, 1.08f);
			card.Scale = Vector2.One * scale;
		}
	}

	private void UpdateLevel(int level)
	{
		_level.Text = ForgeLevels.GetLevelText(level) + " / " + ForgeLevels.MaxLevel;
		foreach (Node child in _levelTrack.GetChildren())
		{
			_levelTrack.RemoveChild(child);
			child.QueueFree();
		}
		for (int i = 1; i <= ForgeLevels.MaxLevel; i++)
		{
			Panel step = new() { CustomMinimumSize = new Vector2(0, 9), SizeFlagsHorizontal = SizeFlags.ExpandFill };
			step.AddThemeStyleboxOverride("panel", Plate(i <= level ? Brass : Soot, Patina, 0, 1, 2));
			_levelTrack.AddChild(step);
		}
	}

	internal Task<List<int>> ChooseTarget(CardModel[] targets) => PresentChoice(() =>
	{
		_choiceTitle.Text = T("TARGET_HEADING", "Choose a target");
		_status.Text = T("TARGET", "Your target stays fixed until you finish.");
		for (int i = 0; i < targets.Length; i++)
		{
			int index = i;
			Button button = AddChoice(targets[i]);
			button.Pressed += () => Complete([index]);
		}
	});

	internal Task<List<int>> ChooseMaterials(CardModel target, CardModel[] candidates, int required, bool canForge) => PresentChoice(() =>
	{
		_materials = true;
		_choiceTitle.Text = T("MATERIAL_HEADING", "Choose materials");
		_idle.Hide();
		_targetIdentity.Text = $"#{_deckSlots[target]} · {target.Title}";
		_targetIdentity.Show();
		if (_card == null)
		{
			_card = NCard.Create(target);
			if (_card != null)
			{
				_cardSlot.AddChild(_card);
				_card.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
				_card.MouseBehaviorRecursive = MouseBehaviorRecursiveEnum.Disabled;
				_card.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
				FitCard();
			}
		}
		UpdateLevel(ForgeLevels.GetLevel(target));
		List<int> chosen = [];
		_commit = () =>
		{
			if (canForge && chosen.Count == required)
			{
				Complete(chosen.ToList());
			}
		};
		List<Button> boxes = [];
		for (int i = 0; i < candidates.Length; i++)
		{
			int index = i;
			Button box = AddChoice(candidates[i]);
			box.ToggleMode = true;
			box.Text = "○  " + box.Text;
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
				box.Text = (selected ? "✓  " : "○  ") + CardLabel(candidates[index]);
				UpdateSelection();
			};
			boxes.Add(box);
		}
		UpdateSelection();

		void UpdateSelection()
		{
			string detail = ForgeLevels.GetLevel(target) >= ForgeLevels.MaxLevel
				? T("MAX", "This card is at maximum upgrade level.")
				: canForge
					? string.Format(T("MATERIALS", "Select exactly {0} matching cards ({1}/{0} selected)."), required, chosen.Count)
					: string.Format(T("SHORTAGE", "Need {0} matching cards. Only {1} can be used."), required, candidates.Length);
			_status.Text = detail;
			_confirm.Disabled = !canForge || chosen.Count != required || _choice == null;
			foreach (Button box in boxes)
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
				success ? "Round {0} · Success\nLevel {1} → {2} · Used {3}" : "Round {0} · Failed\nLevel {1} → {2} · Used {3}"), round, from, to, used), 21);
			line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			Color outcome = success ? Brass : new Color("ed9b88");
			line.AddThemeColorOverride("font_color", outcome);
			_history.GetChild<Label>(0).Hide();
			PanelContainer entry = Frame(line, new Color("30251f"), outcome.Darkened(0.45f), 10);
			_history.AddChild(entry);
			_history.MoveChild(entry, 1);
			_status.Text = T("ROUND_RESOLVED", "Round complete.");
			UpdateLevel(to);
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
			bool restore = _materials;
			_rows.Clear();
			foreach (Node child in _choices.GetChildren())
			{
				_choices.RemoveChild(child);
				child.QueueFree();
			}
			_finish.Disabled = false;
			render();
			if (_materials)
			{
				if (!restore)
				{
					_focusedCard = null;
					_savedScroll = 0;
				}
				Callable.From(() => RestoreMaterialFocus(choice)).CallDeferred();
			}
			else if (IsVisibleInTree())
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
		if (_materials)
		{
			_savedScroll = _choiceScroll.ScrollVertical;
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

	private void RestoreMaterialFocus(TaskCompletionSource<List<int>> choice)
	{
		if (!ReferenceEquals(_choice, choice) || !IsVisibleInTree())
		{
			return;
		}
		int lastSlot = _focusedCard == null ? 0 : _deckSlots[_focusedCard];
		Button? nearest = _rows.Where(row => !row.Value.Disabled)
			.OrderBy(row => Math.Abs(_deckSlots[row.Key] - lastSlot))
			.Select(row => row.Value).FirstOrDefault();
		_choiceScroll.FollowFocus = false;
		_choiceScroll.ScrollVertical = _savedScroll;
		(nearest ?? _finish).GrabFocus();
		_choiceScroll.FollowFocus = true;
	}

	private Button AddChoice(CardModel card)
	{
		Button button = new()
		{
			Text = CardLabel(card),
			TooltipText = CardLabel(card) + "\n" + card.GetDescriptionForPile(PileType.None),
			Alignment = HorizontalAlignment.Left,
			TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
			CustomMinimumSize = new Vector2(0, 58),
		};
		StyleButton(button, false, 22);
		button.FocusEntered += () => _focusedCard = card;
		_rows.Add(card, button);
		_choices.AddChild(button);
		return button;
	}

	private static ScrollContainer Scroll(VBoxContainer content)
	{
		ScrollContainer scroll = new()
		{
			SizeFlagsVertical = SizeFlags.ExpandFill,
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
			FollowFocus = true,
		};
		content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroll.AddChild(content);
		return scroll;
	}

	private static VBoxContainer Column()
	{
		VBoxContainer column = new();
		column.AddThemeConstantOverride("separation", 12);
		return column;
	}

	private static Label Heading(string text, int size)
	{
		Label label = new() { Text = text, VerticalAlignment = VerticalAlignment.Center };
		label.ApplyLocaleFontSubstitution(FontType.Regular, "font");
		label.AddThemeFontSizeOverride("font_size", size);
		label.AddThemeColorOverride("font_color", size >= 27 ? Brass : Ink);
		return label;
	}

	private string CardLabel(CardModel card)
	{
		List<string> details = [$"#{_deckSlots[card]}", ForgeLevels.GetLevelText(ForgeLevels.GetLevel(card))];
		if (card.Enchantment is { } enchantment)
		{
			details.Add(enchantment.Title.GetFormattedText());
		}
		if (card.Affliction is { } affliction)
		{
			details.Add(affliction.Title.GetFormattedText());
		}
		details.Insert(1, card.Title);
		return string.Join("  ·  ", details);
	}

	private static string T(string key, string fallback) => ForgeLoc.Text("FORGE." + key, fallback);
}
