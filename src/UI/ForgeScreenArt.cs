using Godot;
using MegaCrit.Sts2.Core.Localization.Fonts;

namespace Sts2Forge.UI;

internal sealed partial class ForgeScreen
{
	private static readonly Color Ink = new("f6e5cc");
	private static readonly Color Muted = new("cdbba7");
	private static readonly Color Brass = new("efb66f");
	private static readonly Color Iron = new("211e1c");
	private static readonly Color Patina = new("665043");
	private static readonly Color Soot = new("141313");
	private Texture2D? _backdrop;
	private readonly TextureRect _stageLight = new() { MouseFilter = MouseFilterEnum.Ignore, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize };

	public override void _Draw()
	{
		_backdrop ??= Glow(new Color("48291f"), new Color("100f10"), new Vector2(0.5f, 0.75f));
		DrawTextureRect(_backdrop, new Rect2(Vector2.Zero, Size), false);
		for (int edge = 0; edge < 2; edge++)
		{
			float x = edge == 0 ? 18 : Size.X - 18;
			DrawLine(new Vector2(x, 18), new Vector2(x, Size.Y - 18), Patina.Darkened(0.45f), 2);
		}
		for (int i = 0; i < 36; i++)
		{
			float x = Size.X * ((i * 109 + 37) % 997) / 997;
			float y = Size.Y * (0.53f + 0.45f * ((i * 73 + 29) % 107) / 107);
			DrawLine(new Vector2(x, y), new Vector2(x + 3, y - 5 - i % 4), new Color(0.96f, 0.49f, 0.23f, (i % 5 + 1) * 0.09f), 1.5f, true);
		}
	}

	private static Texture2D Glow(Color inner, Color outer, Vector2 origin) => new GradientTexture2D
	{
		Width = 256,
		Height = 256,
		Fill = GradientTexture2D.FillEnum.Radial,
		FillFrom = origin,
		FillTo = new Vector2(1, 0),
		Gradient = new Gradient { Colors = [inner, outer] },
	};

	private static StyleBoxFlat Plate(Color fill, Color edge, int inset = 14, int stroke = 1, int bevel = 7)
	{
		StyleBoxFlat style = new()
		{
			BgColor = fill,
			BorderColor = edge,
			BorderBlend = true,
			ContentMarginLeft = inset,
			ContentMarginRight = inset,
			ContentMarginTop = inset,
			ContentMarginBottom = inset,
		};
		style.SetBorderWidthAll(stroke);
		style.BorderWidthTop = stroke + 1;
		style.SetCornerRadiusAll(bevel);
		return style;
	}

	private static PanelContainer Frame(Control content, Color fill, Color edge, int inset = 16)
	{
		PanelContainer frame = new();
		frame.AddThemeStyleboxOverride("panel", Plate(fill, edge, inset));
		frame.AddChild(content);
		return frame;
	}

	private static void StyleButton(Button button, bool primary, int size)
	{
		Color face = primary ? new Color("78412a") : new Color("2e2723");
		Color edge = primary ? Brass : Patina;
		button.ApplyLocaleFontSubstitution(FontType.Regular, "font");
		button.AddThemeFontSizeOverride("font_size", size);
		button.AddThemeColorOverride("font_color", Ink);
		button.AddThemeColorOverride("font_hover_color", Colors.White);
		button.AddThemeColorOverride("font_pressed_color", Ink);
		button.AddThemeColorOverride("font_focus_color", Ink);
		button.AddThemeColorOverride("font_disabled_color", new Color("a99c8f"));
		button.AddThemeStyleboxOverride("normal", Plate(face, edge, 9));
		button.AddThemeStyleboxOverride("hover", Plate(face.Lightened(0.13f), Brass, 9));
		button.AddThemeStyleboxOverride("pressed", Plate(new Color("694326"), Brass, 9));
		button.AddThemeStyleboxOverride("hover_pressed", Plate(new Color("80532e"), Brass, 9));
		button.AddThemeStyleboxOverride("disabled", Plate(new Color("211e1c"), new Color("46382e"), 9));
		StyleBoxFlat focus = Plate(Colors.Transparent, Brass.Lightened(0.23f), 9, 2);
		focus.DrawCenter = false;
		button.AddThemeStyleboxOverride("focus", focus);
	}
}
