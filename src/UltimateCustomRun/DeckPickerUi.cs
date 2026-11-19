using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace UltimateCustomRun;

// The modifiers that replace or empty the starting deck read as one choice: a two-column radio grid and one
// sentence describing the chosen deck, with its numbers editable in place. The native rows stay in the list
// (hidden) as the source of truth for saves, loadouts and co-op sync; the picker only ticks them.
internal static class DeckPickerUi
{
    internal const string NodeName = "StartingDeckPicker";
    private const int TextSize = 20;

    private enum Choice { Starter, Draft, Sealed, Insanity, Empty }
    private static readonly Choice[] Choices = [Choice.Starter, Choice.Draft, Choice.Sealed, Choice.Insanity, Choice.Empty];

    private sealed class Option
    {
        internal required NButton Button;
        internal required Control Visuals;
        internal required TextureRect Radio;
        internal required ShaderMaterial Material;
        internal required Label Name;
        internal bool Selected;
        internal bool Hovered;
        internal Tween? Tween;
    }

    private sealed class State
    {
        internal readonly Dictionary<Choice, NRunModifierTickbox> Rows = [];
        internal readonly Dictionary<Choice, Option> Options = [];
        internal required HFlowContainer Sentence;
        // The numbers in the current sentence; changing a value updates them in place so focus stays put.
        internal readonly List<(Label Label, bool Pool)> Numbers = [];
        internal bool Editable = true;
        internal string Signature = "";
        internal Action? LayoutChanged;
    }

    private static readonly ConditionalWeakTable<NCustomRunModifiersList, State> States = new();
    private static readonly System.Reflection.MethodInfo AfterModifiersChanged =
        AccessTools.Method(typeof(NCustomRunModifiersList), "AfterModifiersChanged");

    internal static bool IsDeckModifier(ModifierModel? modifier) => ChoiceOf(modifier) != null;

    private static Choice? ChoiceOf(ModifierModel? modifier) => modifier switch
    {
        Draft => Choice.Draft,
        SealedDeck => Choice.Sealed,
        Insanity => Choice.Insanity,
        NoStarterDeck => Choice.Empty,
        _ => null
    };

    private static bool Russian => LocManager.Instance.CultureInfo.TwoLetterISOLanguageName == "ru";

    // {0} is the deck's main number and {1} the Sealed Deck pool; each becomes a value (or stepper).
    private static string Template(Choice choice) => (choice, Russian) switch
    {
        (Choice.Starter, false) => "your character's normal starting deck.",
        (Choice.Starter, true) => "обычная стартовая колода персонажа.",
        (Choice.Draft, false) => "start with no cards and pick {0} card rewards to build your deck.",
        (Choice.Draft, true) => "начать без карт и выбрать {0} наград с картами, чтобы собрать колоду.",
        (Choice.Sealed, false) => "choose {0} cards for your deck from a fixed pool of {1} cards.",
        (Choice.Sealed, true) => "выбрать {0} карт в колоду из пула в {1} карт.",
        (Choice.Insanity, false) => "start with {0} random cards instead of your starting deck.",
        (Choice.Insanity, true) => "начать с {0} случайными картами вместо стартовой колоды.",
        (_, false) => "start with no cards. Other effects still add theirs.",
        (_, true) => "начать без карт. Другие эффекты всё равно добавят свои."
    };

    private static string Title(Choice choice, State state) => choice switch
    {
        Choice.Starter => Russian ? "Обычная" : "Starter",
        Choice.Empty => Russian ? "Пустая" : "Empty",
        _ => System.Text.RegularExpressions.Regex.Replace(
            state.Rows[choice].Modifier!.Title.GetFormattedText(), @"\[[^\]]*\]", "").Trim()
    };

    internal static void Create(NCustomRunModifiersList list, VBoxContainer childList, List<NRunModifierTickbox> rows,
        Dictionary<NRunModifierTickbox, List<Control>> attachments, Action layoutChanged)
    {
        var rowsByChoice = new Dictionary<Choice, NRunModifierTickbox>();
        foreach (var row in rows)
            if (ChoiceOf(row.Modifier) is { } choice && !rowsByChoice.ContainsKey(choice))
                rowsByChoice[choice] = row;
        // Without a deck-replacing modifier there is nothing to choose between.
        if (rowsByChoice.Count == 0) return;

        var root = new VBoxContainer { Name = NodeName, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        root.AddThemeConstantOverride("separation", 6);
        var heading = VanillaUi.Label(Russian ? "Стартовая колода" : "Starting deck", 26, VanillaUi.Gold);
        heading.CustomMinimumSize = new Vector2(0, 40);
        VanillaUi.Shadow(heading);
        root.AddChild(heading);

        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 2);
        root.AddChild(grid);

        var detail = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        detail.AddChild(VanillaUi.Box(new Color(0, 0, 0, 0.35f)));
        var padding = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        foreach (var side in new[] { "margin_left", "margin_right" }) padding.AddThemeConstantOverride(side, 14);
        foreach (var side in new[] { "margin_top", "margin_bottom" }) padding.AddThemeConstantOverride(side, 8);
        detail.AddChild(padding);
        var sentence = new HFlowContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        sentence.AddThemeConstantOverride("h_separation", 6);
        sentence.AddThemeConstantOverride("v_separation", 2);
        padding.AddChild(sentence);
        root.AddChild(detail);
        root.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6), MouseFilter = Control.MouseFilterEnum.Ignore });

        var state = new State { Sentence = sentence, LayoutChanged = layoutChanged };
        foreach (var (choice, row) in rowsByChoice) state.Rows[choice] = row;
        States.AddOrUpdate(list, state);

        // Column-major order, so the list reads top to bottom like the mockup.
        var shown = Choices.Where(choice => choice == Choice.Starter || state.Rows.ContainsKey(choice)).ToList();
        var perColumn = (shown.Count + 1) / 2;
        for (var i = 0; i < perColumn; i++)
        for (var column = 0; column < 2; column++)
        {
            var index = column * perColumn + i;
            if (index >= shown.Count)
            {
                grid.AddChild(new Control { MouseFilter = Control.MouseFilterEnum.Ignore });
                continue;
            }
            var choice = shown[index];
            var option = CreateOption(Title(choice, state));
            grid.AddChild(option.Button);
            state.Options[choice] = option;
            option.Button.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => Select(list, state, choice)));
        }

        // The picker leads the Start tab. The hidden deck rows and their (now unused) value rows follow it.
        childList.AddChild(root);
        childList.MoveChild(root, 0);
        var position = 1;
        foreach (var choice in Choices)
        {
            if (!state.Rows.TryGetValue(choice, out var row)) continue;
            row.Visible = false;
            row.FocusMode = Control.FocusModeEnum.None;
            childList.MoveChild(row, position++);
            foreach (var attached in attachments.GetValueOrDefault(row) ?? [])
                childList.MoveChild(attached, position++);
        }
    }

    private static Option CreateOption(string title)
    {
        var button = new NButton
        {
            Name = "DeckChoice", CustomMinimumSize = new Vector2(0, 52), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.All, MouseFilter = Control.MouseFilterEnum.Stop
        };
        var line = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", 10);
        button.AddChild(line);
        line.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        // Sized like the modifier rows' checkbox: a 48px image at the tickbox scene's 0.8 base scale.
        // Containers reset their direct children's scale on every layout, so the scaled node sits one level down.
        var slot = new Control
        {
            CustomMinimumSize = new Vector2(48, 48), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        var visuals = new Control
        {
            Size = new Vector2(48, 48), PivotOffset = new Vector2(24, 24), Scale = Vector2.One * RadioScale,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        slot.AddChild(visuals);
        var material = new ShaderMaterial { Shader = ResourceLoader.Load<Shader>("res://shaders/hsv.gdshader") };
        material.SetShaderParameter("h", 1.0f);
        material.SetShaderParameter("s", 1.0f);
        material.SetShaderParameter("v", 1.0f);
        var radio = new TextureRect
        {
            Texture = VanillaUi.Radio(false), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, Material = material,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore
        };
        visuals.AddChild(radio);
        radio.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        line.AddChild(slot);
        var name = VanillaUi.Label(title, 22, VanillaUi.Cream, bold: false);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        name.ClipText = true;
        line.AddChild(name);

        var option = new Option { Button = button, Visuals = visuals, Radio = radio, Material = material, Name = name };
        var mouseInside = false;
        void Enter()
        {
            if (option.Hovered) return;
            option.Hovered = true;
            Animate(option, HoverScale, HoverV, 0.05, quick: true);
            Paint(option);
        }
        void Exit()
        {
            if (!option.Hovered || button.HasFocus() || mouseInside) return;
            option.Hovered = false;
            Animate(option, 1, 1, 0.5, quick: false);
            Paint(option);
        }
        // The checkbox's press: shrink and darken, then pop back to the hover state on release.
        button.GuiInput += input =>
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
                Animate(option, PressScale, 0.8f, 0.5, quick: false);
        };
        button.Connect(NClickableControl.SignalName.Released,
            Callable.From<NButton>(_ => Animate(option, HoverScale, HoverV, 0.05, quick: true)));
        button.MouseEntered += () => { mouseInside = true; Enter(); };
        button.MouseExited += () => { mouseInside = false; Exit(); };
        button.FocusEntered += Enter;
        button.FocusExited += Exit;
        Paint(option);
        return option;
    }

    // NTickbox's numbers: base scale 0.8, hover x1.05 and brightness 1.2, press x0.95 and brightness 0.8.
    private const float RadioScale = 0.8f, HoverScale = 1.05f, PressScale = 0.95f, HoverV = 1.2f;

    private static void Animate(Option option, float scale, float brightness, double seconds, bool quick)
    {
        option.Tween?.Kill();
        if (!GodotObject.IsInstanceValid(option.Visuals) || !option.Visuals.IsInsideTree()) return;
        option.Tween = option.Visuals.CreateTween().SetParallel();
        var from = (float)option.Material.GetShaderParameter("v");
        var size = option.Tween.TweenProperty(option.Visuals, "scale", Vector2.One * RadioScale * scale, seconds);
        var light = option.Tween.TweenMethod(Callable.From<float>(value => option.Material.SetShaderParameter("v", value)),
            from, brightness, seconds);
        if (quick) return;
        size.SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Expo);
        light.SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Expo);
    }

    private static void Paint(Option option)
    {
        if (!GodotObject.IsInstanceValid(option.Button)) return;
        option.Radio.Texture = VanillaUi.Radio(option.Selected);
        option.Name.AddThemeColorOverride("font_color", option.Selected || option.Hovered ? VanillaUi.Gold : VanillaUi.Cream);
    }

    internal static void Refresh(NCustomRunModifiersList list)
    {
        if (!GodotObject.IsInstanceValid(list) || !States.TryGetValue(list, out var state)) return;
        var current = Current(state);
        state.Editable = ModifierGroupsUi.IsEditable(list);
        foreach (var (choice, option) in state.Options)
        {
            if (!GodotObject.IsInstanceValid(option.Button)) continue;
            option.Selected = choice == current;
            // Clients see the host's deck but cannot change it.
            option.Button.MouseFilter = state.Editable ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
            option.Button.FocusMode = state.Editable ? Control.FocusModeEnum.All : Control.FocusModeEnum.None;
            Paint(option);
        }
        BuildSentence(list, state, current);
    }

    private static void BuildSentence(NCustomRunModifiersList list, State state, Choice choice)
    {
        if (!GodotObject.IsInstanceValid(state.Sentence)) return;
        var modifier = state.Rows.GetValueOrDefault(choice)?.Modifier;
        var adjustable = state.Editable;
        var values = new int[2];
        if (modifier != null && ModifierValues.For(modifier) != null) values[0] = ModifierValues.Get(modifier);
        if (modifier is SealedDeck) values[1] = ModifierValues.GetSealedPool(modifier);
        var signature = $"{choice}|{adjustable}|{Russian}";
        if (signature == state.Signature)
        {
            foreach (var (label, pool) in state.Numbers)
                if (GodotObject.IsInstanceValid(label)) label.Text = values[pool ? 1 : 0].ToString();
            return;
        }
        state.Signature = signature;
        state.Numbers.Clear();

        foreach (var child in state.Sentence.GetChildren()) child.QueueFree();
        state.Sentence.AddChild(Word(Title(choice, state) + ":", VanillaUi.Gold, bold: true));
        foreach (var token in Template(choice).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var match = System.Text.RegularExpressions.Regex.Match(token, @"^\{(\d)\}(.*)$");
            if (!match.Success || modifier == null)
            {
                state.Sentence.AddChild(Word(token, new Color(VanillaUi.Cream, 0.62f)));
                continue;
            }
            var pool = match.Groups[1].Value == "1";
            var value = values[pool ? 1 : 0];
            var holder = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            holder.AddThemeConstantOverride("separation", 0);
            var number = Word(value.ToString(), VanillaUi.Blue, bold: true);
            state.Numbers.Add((number, pool));
            holder.AddChild(adjustable ? Stepper(number, step => ModifierValueUi.Adjust(list, modifier, pool, step)) : number);
            if (match.Groups[2].Value.Length > 0) holder.AddChild(Word(match.Groups[2].Value, new Color(VanillaUi.Cream, 0.62f)));
            state.Sentence.AddChild(holder);
        }
        Callable.From(() => state.LayoutChanged?.Invoke()).CallDeferred();
    }

    private static Label Word(string text, Color color, bool bold = false)
    {
        var label = VanillaUi.Label(text, TextSize, color, bold);
        label.CustomMinimumSize = new Vector2(0, 34);
        return label;
    }

    // ‹ value › with the value in the game's number blue; arrows brighten on hover.
    private static Control Stepper(Label number, Action<int> step)
    {
        var box = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        box.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0.45f), BorderColor = new Color(VanillaUi.Blue, 0.35f),
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 2, ContentMarginRight = 2
        });
        var line = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", 0);
        box.AddChild(line);
        line.AddChild(Arrow("‹", () => step(-1)));
        number.HorizontalAlignment = HorizontalAlignment.Center;
        number.CustomMinimumSize = new Vector2(30, 30);
        line.AddChild(number);
        line.AddChild(Arrow("›", () => step(1)));
        return box;
    }

    private static NButton Arrow(string glyph, Action pressed)
    {
        var button = new NButton
        {
            Name = "Step", CustomMinimumSize = new Vector2(22, 30), FocusMode = Control.FocusModeEnum.All,
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        var label = VanillaUi.Label(glyph, 24, VanillaUi.Gold);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        button.AddChild(label);
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        // Angle quotes sit low in Kreon; lift them to the digits' optical center.
        label.OffsetTop = -5;
        label.OffsetBottom = -5;
        void Light(bool on) => label.AddThemeColorOverride("font_color", on ? VanillaUi.Cream : VanillaUi.Gold);
        button.MouseEntered += () => Light(true);
        button.MouseExited += () => Light(false);
        button.FocusEntered += () => Light(true);
        button.FocusExited += () => Light(false);
        button.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => pressed()));
        return button;
    }

    // Old loadouts may tick more than one deck modifier; the first replacement wins, as it does at Neow.
    private static Choice Current(State state)
    {
        foreach (var choice in Choices)
            if (state.Rows.TryGetValue(choice, out var row) && row.IsTicked) return choice;
        return Choice.Starter;
    }

    private static void Select(NCustomRunModifiersList list, State state, Choice choice)
    {
        if (!state.Editable || Current(state) == choice) return;
        SfxCmd.Play("event:/sfx/ui/clicks/ui_checkbox_on");
        foreach (var (rowChoice, row) in state.Rows) row.IsTicked = rowChoice == choice;
        var source = state.Rows.GetValueOrDefault(choice) ?? state.Rows.Values.First();
        // Same path as a native click: exclusivity, value rows, start order and co-op sync all follow.
        AfterModifiersChanged.Invoke(list, [source]);
        Refresh(list);
        state.LayoutChanged?.Invoke();
    }
}
