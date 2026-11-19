using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace UltimateCustomRun;

// The modifiers that replace or empty the starting deck read as one choice. Their native rows stay in the
// list as the source of truth for saves, loadouts and co-op sync; this picker only ticks them.
internal static class DeckPickerUi
{
    internal const string NodeName = "StartingDeckPicker";
    private const string StarterTitleKey = "ultimate_custom_run.deck.starter";
    private const string StarterDescriptionKey = "ultimate_custom_run.deck.starter_description";

    private enum Choice { Starter, Draft, Sealed, Insanity, Empty }
    private static readonly Choice[] Choices = [Choice.Starter, Choice.Draft, Choice.Sealed, Choice.Insanity, Choice.Empty];

    private sealed class State
    {
        internal readonly Dictionary<Choice, NRunModifierTickbox> Rows = [];
        internal readonly Dictionary<Choice, NRunModifierTickbox> Options = [];
        internal bool Applying;
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
        root.AddThemeConstantOverride("separation", 4);
        var heading = new Label
        {
            Text = Russian ? "Стартовая колода" : "Starting deck",
            VerticalAlignment = VerticalAlignment.Center, CustomMinimumSize = new Vector2(0, 44),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        heading.AddThemeFontOverride("font", ResourceLoader.Load<Font>("res://themes/kreon_bold_shared.tres"));
        heading.AddThemeFontSizeOverride("font_size", 26);
        heading.AddThemeColorOverride("font_color", new Color(0.937255f, 0.784314f, 0.317647f));
        heading.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.5f));
        heading.AddThemeConstantOverride("shadow_offset_x", 3);
        heading.AddThemeConstantOverride("shadow_offset_y", 2);
        var headingMargin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        headingMargin.AddThemeConstantOverride("margin_left", 8);
        headingMargin.AddChild(heading);
        root.AddChild(headingMargin);

        var grid = new GridContainer { Columns = 3, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 4);
        root.AddChild(grid);

        var template = state.Rows.Values.First().Modifier!;
        foreach (var choice in Choices)
        {
            if (choice != Choice.Starter && !state.Rows.ContainsKey(choice)) continue;
            var source = choice == Choice.Starter ? template : state.Rows[choice].Modifier!;
            var option = NRunModifierTickbox.Create((ModifierModel)source.MutableClone())
                ?? throw new InvalidOperationException("Could not create a starting deck option.");
            option.Name = "Deck" + choice;
            option.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            option.CustomMinimumSize = new Vector2(120, 48);
            option.FocusMode = Control.FocusModeEnum.All;
            grid.AddChild(option);
            // Tickbox _Ready writes the full modifier sentence; the picker shows only the name.
            option.Ready += () => SetLabel(option, choice, state);
            if (option.IsNodeReady()) SetLabel(option, choice, state);
            state.Options[choice] = option;
            var tipChoice = choice;
            option.MouseEntered += () => ShowTooltip(option, tipChoice, state);
            option.MouseExited += () => NHoverTipSet.Remove(option);
            option.Toggled += toggled =>
            {
                if (state.Applying) return;
                if (toggled.IsTicked) Select(list, state, tipChoice);
                else Refresh(list); // A radio choice cannot be cleared by clicking it again.
            };
        }

        // The picker leads the Start tab; the hidden deck rows and their value sliders follow it, so a
        // selected Draft or Sealed Deck shows its settings right below the choice.
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

    internal static void Refresh(NCustomRunModifiersList list)
    {
        if (!GodotObject.IsInstanceValid(list) || !States.TryGetValue(list, out var state)) return;
        var current = Current(state);
        var editable = ModifierGroupsUi.IsEditable(list);
        state.Applying = true;
        try
        {
            foreach (var (choice, option) in state.Options)
            {
                if (!GodotObject.IsInstanceValid(option)) continue;
                if (option.IsNodeReady()) option.IsTicked = choice == current;
                // Clients see the host's deck but cannot change it.
                option.MouseFilter = editable ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
                option.FocusMode = editable ? Control.FocusModeEnum.All : Control.FocusModeEnum.None;
                if (!editable) NHoverTipSet.Remove(option);
            }
        }
        finally { state.Applying = false; }
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
        if (!ModifierGroupsUi.IsEditable(list)) { Refresh(list); return; }
        state.Applying = true;
        try
        {
            foreach (var (rowChoice, row) in state.Rows) row.IsTicked = rowChoice == choice;
        }
        finally { state.Applying = false; }
        var source = state.Rows.GetValueOrDefault(choice) ?? state.Rows.Values.First();
        // Same path as a native click: exclusivity, value rows, start order and co-op sync all follow.
        AfterModifiersChanged.Invoke(list, [source]);
        Refresh(list);
        state.LayoutChanged?.Invoke();
    }

    private static void SetLabel(NRunModifierTickbox option, Choice choice, State state)
    {
        if (!GodotObject.IsInstanceValid(option)) return;
        var text = choice switch
        {
            Choice.Starter => new LocString("modifiers", StarterTitleKey).GetFormattedText(),
            Choice.Empty => Russian ? "Пустая" : "Empty",
            _ => state.Rows[choice].Modifier!.Title.GetFormattedText()
        };
        option.GetNode<MegaCrit.Sts2.addons.mega_text.MegaRichTextLabel>("HBoxContainer/Description").Text = text;
    }

    private static void ShowTooltip(NRunModifierTickbox option, Choice choice, State state)
    {
        if (option.MouseFilter == Control.MouseFilterEnum.Ignore) return;
        NHoverTipSet.Remove(option);
        var tip = choice == Choice.Starter
            ? new HoverTip(new LocString("modifiers", StarterTitleKey), new LocString("modifiers", StarterDescriptionKey), null!)
            : new HoverTip(state.Rows[choice].Modifier!.Title, state.Rows[choice].Modifier!.Description, null!);
        NHoverTipSet.CreateAndShow(option, tip, HoverTipAlignment.Right);
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
