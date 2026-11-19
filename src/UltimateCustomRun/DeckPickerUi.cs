using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace UltimateCustomRun;

// The modifiers that replace or empty the starting deck read as one choice of cards. Their native rows stay
// in the list (hidden) as the source of truth for saves, loadouts and co-op sync; the cards only tick them.
internal static class DeckPickerUi
{
    internal const string NodeName = "StartingDeckPicker";
    private const string StarterTitleKey = "ultimate_custom_run.deck.starter";
    private const string StarterDescriptionKey = "ultimate_custom_run.deck.starter_description";

    private enum Choice { Starter, Draft, Sealed, Insanity, Empty }
    private static readonly Choice[] Choices = [Choice.Starter, Choice.Draft, Choice.Sealed, Choice.Insanity, Choice.Empty];

    private sealed class Card
    {
        internal required NButton Button;
        internal required NinePatchRect Background;
        internal required Panel Frame;
        internal required Label Title;
        internal bool Selected;
        internal bool Hovered;
        internal Tween? Tween;
    }

    private sealed class State
    {
        internal readonly Dictionary<Choice, NRunModifierTickbox> Rows = [];
        internal readonly Dictionary<Choice, Card> Cards = [];
        internal bool Editable = true;
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

    private static string Summary(Choice choice) => (choice, Russian) switch
    {
        (Choice.Starter, false) => "Your character's normal deck.",
        (Choice.Starter, true) => "Обычная колода персонажа.",
        (Choice.Draft, false) => "Build it from card rewards.",
        (Choice.Draft, true) => "Собрать из наград с картами.",
        (Choice.Sealed, false) => "Pick cards from a fixed pool.",
        (Choice.Sealed, true) => "Выбрать карты из пула.",
        (Choice.Insanity, false) => "A deck of random cards.",
        (Choice.Insanity, true) => "Колода из случайных карт.",
        (_, false) => "No cards. Other effects add theirs.",
        (_, true) => "Без карт. Их добавят другие эффекты."
    };

    internal static void Create(NCustomRunModifiersList list, VBoxContainer childList, List<NRunModifierTickbox> rows,
        Dictionary<NRunModifierTickbox, List<Control>> attachments, Action layoutChanged)
    {
        var state = new State { LayoutChanged = layoutChanged };
        foreach (var row in rows)
            if (ChoiceOf(row.Modifier) is { } choice && !state.Rows.ContainsKey(choice))
                state.Rows[choice] = row;
        // Without a deck-replacing modifier there is nothing to choose between.
        if (state.Rows.Count == 0) return;
        States.AddOrUpdate(list, state);
        RegisterStarterText();

        var root = new VBoxContainer { Name = NodeName, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        root.AddThemeConstantOverride("separation", 8);
        var heading = VanillaUi.Label(Russian ? "Стартовая колода" : "Starting deck", 26, VanillaUi.Gold);
        heading.CustomMinimumSize = new Vector2(0, 40);
        VanillaUi.Shadow(heading);
        root.AddChild(heading);

        var grid = new GridContainer { Columns = 3, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 10);
        root.AddChild(grid);
        foreach (var choice in Choices)
        {
            if (choice != Choice.Starter && !state.Rows.ContainsKey(choice)) continue;
            var title = choice switch
            {
                Choice.Starter => new LocString("modifiers", StarterTitleKey).GetFormattedText(),
                Choice.Empty => Russian ? "Пустая" : "Empty",
                _ => state.Rows[choice].Modifier!.Title.GetFormattedText()
            };
            var card = CreateCard(title, Summary(choice));
            grid.AddChild(card.Button);
            state.Cards[choice] = card;
            var picked = choice;
            card.Button.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => Select(list, state, picked)));
            card.Button.MouseEntered += () => ShowTooltip(card.Button, picked, state);
            card.Button.MouseExited += () => NHoverTipSet.Remove(card.Button);
        }
        root.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4), MouseFilter = Control.MouseFilterEnum.Ignore });

        // The picker leads the Start tab; the hidden deck rows and their value sliders follow it, so a
        // selected Draft or Sealed Deck shows its settings right below the cards.
        childList.AddChild(root);
        childList.MoveChild(root, 0);
        var index = 1;
        foreach (var choice in Choices)
        {
            if (!state.Rows.TryGetValue(choice, out var row)) continue;
            row.Visible = false;
            row.FocusMode = Control.FocusModeEnum.None;
            childList.MoveChild(row, index++);
            foreach (var attached in attachments.GetValueOrDefault(row) ?? [])
                childList.MoveChild(attached, index++);
        }
    }

    private static Card CreateCard(string title, string summary)
    {
        var button = new NButton
        {
            Name = "DeckChoice", CustomMinimumSize = new Vector2(0, 96), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.All, MouseFilter = Control.MouseFilterEnum.Stop
        };
        button.Resized += () => button.PivotOffset = button.Size / 2;
        var background = VanillaUi.Box(new Color(0, 0, 0, 0.35f));
        button.AddChild(background);
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var frame = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        frame.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(VanillaUi.Gold, 0.08f), BorderColor = VanillaUi.Gold,
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10, CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10
        });
        button.AddChild(frame);
        frame.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", 14);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        button.AddChild(margin);
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 0);
        margin.AddChild(column);
        var titleLabel = VanillaUi.Label(title, 24, VanillaUi.Cream);
        VanillaUi.Outline(titleLabel, 8);
        titleLabel.ClipText = true;
        titleLabel.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        column.AddChild(titleLabel);
        var summaryLabel = VanillaUi.Label(summary, 18, new Color(VanillaUi.Cream, 0.62f), bold: false);
        summaryLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        summaryLabel.VerticalAlignment = VerticalAlignment.Top;
        summaryLabel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        column.AddChild(summaryLabel);

        var card = new Card { Button = button, Background = background, Frame = frame, Title = titleLabel };
        var mouseInside = false;
        void Enter()
        {
            if (card.Hovered) return;
            card.Hovered = true;
            VanillaUi.HoverIn(button, null, ref card.Tween);
            Paint(card);
        }
        void Exit()
        {
            if (!card.Hovered || button.HasFocus() || mouseInside) return;
            card.Hovered = false;
            VanillaUi.HoverOut(button, null, ref card.Tween);
            Paint(card);
        }
        button.MouseEntered += () => { mouseInside = true; Enter(); };
        button.MouseExited += () => { mouseInside = false; Exit(); };
        button.FocusEntered += Enter;
        button.FocusExited += Exit;
        Paint(card);
        return card;
    }

    private static void Paint(Card card)
    {
        if (!GodotObject.IsInstanceValid(card.Button)) return;
        card.Frame.Visible = card.Selected;
        card.Background.Modulate = new Color(0, 0, 0, card.Hovered ? 0.55f : 0.35f);
        card.Title.AddThemeColorOverride("font_color", card.Selected || card.Hovered ? VanillaUi.Gold : VanillaUi.Cream);
    }

    internal static void Refresh(NCustomRunModifiersList list)
    {
        if (!GodotObject.IsInstanceValid(list) || !States.TryGetValue(list, out var state)) return;
        var current = Current(state);
        state.Editable = ModifierGroupsUi.IsEditable(list);
        foreach (var (choice, card) in state.Cards)
        {
            if (!GodotObject.IsInstanceValid(card.Button)) continue;
            card.Selected = choice == current;
            // Clients see the host's deck but cannot change it.
            card.Button.MouseFilter = state.Editable ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
            card.Button.FocusMode = state.Editable ? Control.FocusModeEnum.All : Control.FocusModeEnum.None;
            if (!state.Editable) NHoverTipSet.Remove(card.Button);
            Paint(card);
        }
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
        foreach (var (rowChoice, row) in state.Rows) row.IsTicked = rowChoice == choice;
        var source = state.Rows.GetValueOrDefault(choice) ?? state.Rows.Values.First();
        // Same path as a native click: exclusivity, value rows, start order and co-op sync all follow.
        AfterModifiersChanged.Invoke(list, [source]);
        Refresh(list);
        state.LayoutChanged?.Invoke();
    }

    private static void ShowTooltip(Control owner, Choice choice, State state)
    {
        if (!state.Editable) return;
        NHoverTipSet.Remove(owner);
        var tip = choice == Choice.Starter
            ? new HoverTip(new LocString("modifiers", StarterTitleKey), new LocString("modifiers", StarterDescriptionKey), null!)
            : new HoverTip(state.Rows[choice].Modifier!.Title, state.Rows[choice].Modifier!.Description, null!);
        NHoverTipSet.CreateAndShow(owner, tip, HoverTipAlignment.Left);
    }

    private static void RegisterStarterText()
    {
        LocManager.Instance.GetTable("modifiers").MergeWith(new Dictionary<string, string>
        {
            [StarterTitleKey] = Russian ? "Обычная" : "Starter",
            [StarterDescriptionKey] = Russian
                ? "Начать с обычной стартовой колодой персонажа."
                : "Start with your character's normal starting deck."
        });
    }
}
