using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace UltimateCustomRun;

// A summary of every selected modifier in the left column, so the whole setup is visible without
// opening each tab. In co-op it sits below the lobby players; clicking an entry opens its tab.
internal static class ActiveModifiersUi
{
    private sealed class Panel
    {
        internal required Control Root;
        internal required Control Left;
        internal required Control? Players;
        internal required Label Count;
        internal required Label Empty;
        internal required HFlowContainer Chips;
        internal required Action<ModifierModel> Reveal;
        internal string Signature = "";
    }

    private static readonly ConditionalWeakTable<NCustomRunModifiersList, Panel> Panels = new();
    private static readonly Color Gold = new(0.937255f, 0.784314f, 0.317647f);
    private static readonly Color Cream = new(1, 0.9647059f, 0.8862745f);
    private static readonly Color Red = Color.FromHtml("ff6b64");
    private static readonly StringName ShaderV = new("v");
    // The game mode dropdown and seed row start below this line of the left column.
    private const float Bottom = 516;
    private const float MinimumHeight = 110;

    private static bool Russian => LocManager.Instance.CultureInfo.TwoLetterISOLanguageName == "ru";

    internal static void Create(NCustomRunModifiersList list, Action<ModifierModel> reveal)
    {
        var left = list.GetParent()?.GetParent()?.GetNodeOrNull<Control>("LeftContainer");
        if (left == null) return;
        var players = left.GetNodeOrNull<Control>("RemotePlayerContainer");

        var root = new Control { Name = "ActiveModifiers", MouseFilter = Control.MouseFilterEnum.Ignore, ClipContents = true };
        left.AddChild(root);
        // Same dark nine-patch as the Ascension panel below it.
        var background = new NinePatchRect
        {
            Texture = ResourceLoader.Load<Texture2D>("res://images/ui/tiny_nine_patch.png"),
            PatchMarginLeft = 12, PatchMarginTop = 12, PatchMarginRight = 12, PatchMarginBottom = 12,
            Modulate = new Color(0, 0, 0, 0.5f), MouseFilter = Control.MouseFilterEnum.Ignore
        };
        root.AddChild(background);
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        foreach (var side in new[] { "margin_left", "margin_right" }) margin.AddThemeConstantOverride(side, 20);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_bottom", 14);
        root.AddChild(margin);
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 8);
        margin.AddChild(column);

        var heading = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        heading.AddThemeConstantOverride("separation", 10);
        column.AddChild(heading);
        var title = CreateLabel(Russian ? "Выбранные модификаторы" : "Active modifiers", 28, Gold, "res://themes/kreon_bold_shared.tres");
        title.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.5f));
        title.AddThemeConstantOverride("shadow_offset_x", 3);
        title.AddThemeConstantOverride("shadow_offset_y", 2);
        heading.AddChild(title);
        var count = CreateLabel("", 26, new Color(0.529f, 0.808f, 0.922f), "res://themes/kreon_bold_shared.tres");
        heading.AddChild(count);

        var empty = CreateLabel(Russian ? "Нет модификаторов: обычный забег." : "None. This is a normal run.", 22,
            new Color(Cream, 0.6f), "res://themes/kreon_regular_shared.tres");
        column.AddChild(empty);
        var chips = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        chips.AddThemeConstantOverride("h_separation", 8);
        chips.AddThemeConstantOverride("v_separation", 8);
        column.AddChild(chips);

        var panel = new Panel
        {
            Root = root, Left = left, Players = players, Count = count, Empty = empty, Chips = chips, Reveal = reveal
        };
        Panels.AddOrUpdate(list, panel);
        if (players?.GetNodeOrNull<Container>("Container") is { } flow)
            flow.SortChildren += () => Callable.From(() => Place(panel)).CallDeferred();
        if (players != null) players.VisibilityChanged += () => Place(panel);
        Place(panel);
    }

    internal static void Refresh(NCustomRunModifiersList list)
    {
        if (!GodotObject.IsInstanceValid(list) || !Panels.TryGetValue(list, out var panel) ||
            !GodotObject.IsInstanceValid(panel.Root)) return;
        Place(panel);
        var modifiers = list.GetModifiersTickedOn();
        var signature = string.Join("|", modifiers.Select(modifier => modifier.GetType().FullName + ":" + Text(modifier)));
        if (signature == panel.Signature) return;
        panel.Signature = signature;
        foreach (var child in panel.Chips.GetChildren()) child.QueueFree();
        var negatives = ModelDb.BadModifiers.Select(model => model.GetType()).ToHashSet();
        foreach (var modifier in modifiers)
        {
            var group = ModifierGroups.Classify(modifier, negatives);
            panel.Chips.AddChild(CreateChip(Text(modifier),
                group is ModifierGroup.Negatives or ModifierGroup.Ascension ? Red : Gold,
                () => panel.Reveal(modifier)));
        }
        panel.Count.Text = modifiers.Count > 0 ? modifiers.Count.ToString() : "";
        panel.Empty.Visible = modifiers.Count == 0;
    }

    private static string Text(ModifierModel modifier)
    {
        var text = modifier.Title.GetFormattedText();
        // Titles may carry rich text tags; chips are plain labels.
        return System.Text.RegularExpressions.Regex.Replace(text, @"\[[^\]]*\]", "").Trim();
    }

    // Fills the free space between the lobby players (or the title in singleplayer) and the seed row.
    private static void Place(Panel panel)
    {
        if (!GodotObject.IsInstanceValid(panel.Root)) return;
        var players = panel.Players;
        float left = 263, right = 888, top = 235;
        if (players != null && GodotObject.IsInstanceValid(players))
        {
            left = players.OffsetLeft;
            // Leave a gap before the modifier panel, which starts left of the right column.
            right = Math.Min(players.OffsetRight, 866);
            top = players.OffsetTop;
            if (players.Visible && players.GetNodeOrNull<Control>("Container") is { } flow)
            {
                var used = flow.GetChildren().OfType<Control>().Where(child => child.Visible)
                    .Select(child => child.Position.Y + child.Size.Y).DefaultIfEmpty(0).Max();
                top += flow.Position.Y + used + 16;
            }
        }
        panel.Root.AnchorLeft = panel.Root.AnchorRight = panel.Root.AnchorTop = panel.Root.AnchorBottom = 0;
        panel.Root.OffsetLeft = left;
        panel.Root.OffsetRight = right;
        panel.Root.OffsetTop = top;
        panel.Root.OffsetBottom = Bottom;
        panel.Root.Visible = Bottom - top >= MinimumHeight;
    }

    private static Label CreateLabel(string text, int size, Color color, string font)
    {
        var label = new Label { Text = text, VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontOverride("font", ResourceLoader.Load<Font>(font));
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    // A small version of the card library's sort buttons: the same banner texture, HSV hover and gold text.
    private static NButton CreateChip(string text, Color color, Action pressed)
    {
        var font = ResourceLoader.Load<Font>("res://themes/kreon_bold_shared.tres");
        const int fontSize = 20;
        var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, fontSize).X + 36;
        var chip = new NButton
        {
            Name = "Chip", CustomMinimumSize = new Vector2(width, 40), FocusMode = Control.FocusModeEnum.None,
            MouseFilter = Control.MouseFilterEnum.Stop, MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            PivotOffset = new Vector2(width / 2, 20)
        };
        var material = new ShaderMaterial { Shader = ResourceLoader.Load<Shader>("res://shaders/hsv.gdshader") };
        material.SetShaderParameter("h", 1.0f);
        material.SetShaderParameter("s", 0.8f);
        material.SetShaderParameter(ShaderV, 0.8f);
        var image = new TextureRect
        {
            Texture = ResourceLoader.Load<Texture2D>("res://images/ui/reward_screen/reward_item_button.png"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
            Material = material, Modulate = new Color(0.8f, 0.8f, 0.8f), MouseFilter = Control.MouseFilterEnum.Ignore
        };
        chip.AddChild(image);
        image.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var label = CreateLabel(text, fontSize, color, "res://themes/kreon_bold_shared.tres");
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.5f));
        label.AddThemeConstantOverride("outline_size", 10);
        chip.AddChild(label);
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        label.OffsetBottom = -2;

        Tween? tween = null;
        chip.MouseEntered += () =>
        {
            tween?.Kill();
            material.SetShaderParameter(ShaderV, 1.15f);
            chip.Scale = Vector2.One * 1.05f;
        };
        chip.MouseExited += () =>
        {
            tween?.Kill();
            if (!GodotObject.IsInstanceValid(chip) || !chip.IsInsideTree()) return;
            tween = chip.CreateTween().SetParallel();
            tween.TweenProperty(chip, "scale", Vector2.One, 0.5).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Expo);
            tween.TweenMethod(Callable.From<float>(value => material.SetShaderParameter(ShaderV, value)), 1.15f, 0.8f, 0.5)
                .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Expo);
        };
        chip.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => pressed()));
        return chip;
    }
}
