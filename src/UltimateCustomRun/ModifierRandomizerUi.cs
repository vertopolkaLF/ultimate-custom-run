using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace UltimateCustomRun;

// The Randomize section: per-group count ranges, roll options, and the Roll / Chaos buttons.
internal static class ModifierRandomizerUi
{
    private sealed class RangeRow
    {
        internal required ModifierGroup Group;
        internal required HBoxContainer Root;
        internal required RangeSlider Slider;
        internal required Label Value;
        internal int Total;
        internal bool Applying;
    }

    private sealed class State
    {
        internal List<RangeRow> Ranges = [];
    }

    private static readonly ConditionalWeakTable<NCustomRunModifiersList, State> States = new();

    private static List<NRunModifierTickbox> Tickboxes(NCustomRunModifiersList list) =>
        (List<NRunModifierTickbox>)AccessTools.Field(typeof(NCustomRunModifiersList), "_modifierTickboxes").GetValue(list)!;

    private static bool Russian => LocManager.Instance.CultureInfo.TwoLetterISOLanguageName == "ru";

    internal static bool IsEditable(NCustomRunModifiersList list) =>
        (MultiplayerUiMode)AccessTools.Field(typeof(NCustomRunModifiersList), "_mode").GetValue(list)!
            is MultiplayerUiMode.Singleplayer or MultiplayerUiMode.Host;

    internal static void Create(NCustomRunModifiersList list, VBoxContainer container)
    {
        var state = States.GetOrCreateValue(list);
        var settings = ModifierRandomizer.Settings;
        var russian = Russian;

        var options = new HBoxContainer { Name = "RandomizerOptions", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        options.AddThemeConstantOverride("separation", 12);
        container.AddChild(options);
        AddOption(list, options, "RandomValues",
            russian ? "Случайные значения" : "Random values",
            russian ? "Выпавшие модификаторы получают случайные настройки: числа на ползунках (например, сколько карт даёт Insanity), вариант (Normal / Draft / Pick Any) и доп. опции. Без галочки берутся ваши текущие настройки."
                : "Picked modifiers get random settings: slider numbers (like Insanity's card count), variant (Normal / Draft / Pick Any) and sub-options. Unticked, they use your current settings.",
            () => settings.Values, value => { settings.Values = value; ModifierRandomizer.Save(); });

        var totals = Candidates(list, ignoreLocks: true);
        foreach (var group in ModifierRandomizer.RollGroups)
        {
            var total = totals.TryGetValue(group, out var pool) ? pool.Count : 0;
            if (total == 0) continue;
            var row = CreateRangeRow(list, group, total);
            state.Ranges.Add(row);
            container.AddChild(row.Root);
        }

        var buttons = new HBoxContainer
        {
            Name = "RandomizerButtons", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Alignment = BoxContainer.AlignmentMode.Center, CustomMinimumSize = new Vector2(0, 64)
        };
        buttons.AddThemeConstantOverride("separation", 20);
        container.AddChild(buttons);
        var roll = ModifierPresetUi.CreateButton("RollButton", russian ? "Бросить" : "Roll", 180);
        var chaos = ModifierPresetUi.CreateButton("ChaosRollButton", russian ? "Хаос" : "Chaos", 180);
        chaos.TooltipText = russian ? "Сначала случайные диапазоны, затем бросок" : "Randomize the ranges too, then roll";
        roll.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => Roll(list, chaos: false)));
        chaos.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => Roll(list, chaos: true)));
        buttons.AddChild(roll);
        buttons.AddChild(chaos);
        Refresh(list);
    }

    internal static NRunModifierTickbox AddOption(NCustomRunModifiersList list, Container parent, string name, string title, string description,
        Func<bool> get, Action<bool> set)
    {
        // A display copy only lends the native checkbox scene; it never joins the modifier list.
        var source = Tickboxes(list).First(row => row.Modifier != null).Modifier!;
        var tickbox = NRunModifierTickbox.Create((ModifierModel)source.MutableClone())
            ?? throw new InvalidOperationException("Could not create a randomizer checkbox.");
        tickbox.Name = name;
        tickbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        tickbox.FocusMode = Control.FocusModeEnum.All;
        tickbox.MouseFilter = Control.MouseFilterEnum.Stop;
        var applying = false;
        tickbox.Ready += () =>
        {
            var text = new LocString("main_menu_ui", "CUSTOM_RUN_SCREEN.MODIFIER_LABEL");
            text.Add("color", "gold");
            text.Add("modifier_title", title);
            text.Add("modifier_description", description);
            tickbox.GetNode<MegaCrit.Sts2.addons.mega_text.MegaRichTextLabel>("HBoxContainer/Description").Text = text.GetFormattedText();
            applying = true;
            try { tickbox.IsTicked = get(); }
            finally { applying = false; }
        };
        tickbox.Toggled += toggled =>
        {
            if (applying) return;
            set(toggled.IsTicked);
        };
        parent.AddChild(tickbox);
        return tickbox;
    }

    private static RangeRow CreateRangeRow(NCustomRunModifiersList list, ModifierGroup group, int total)
    {
        var root = new HBoxContainer
        {
            Name = group + "Range", CustomMinimumSize = new Vector2(0, 58),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        root.AddThemeConstantOverride("separation", 12);
        root.AddChild(new Control { CustomMinimumSize = new Vector2(52, 0), MouseFilter = Control.MouseFilterEnum.Ignore });
        var title = ModifierGroups.Sections.First(section => section.Group == group).Title;
        root.AddChild(CreateLabel(title, 200, HorizontalAlignment.Left));
        var slider = new RangeSlider(group + "RangeSlider", total);
        root.AddChild(slider.Root);
        var value = CreateLabel(string.Empty, 90, HorizontalAlignment.Right);
        root.AddChild(value);
        var row = new RangeRow { Group = group, Root = root, Slider = slider, Value = value, Total = total };
        slider.Changed += (min, max) => OnRangeChanged(list, row, min, max);
        return row;
    }

    private static Label CreateLabel(string text, float width, HorizontalAlignment alignment)
    {
        var label = new Label
        {
            Text = text, CustomMinimumSize = new Vector2(width, 0), HorizontalAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeFontOverride("font", ResourceLoader.Load<Font>("res://themes/kreon_regular_shared.tres"));
        label.AddThemeFontSizeOverride("font_size", 22);
        return label;
    }

    private static void OnRangeChanged(NCustomRunModifiersList list, RangeRow row, int min, int max)
    {
        if (row.Applying || !IsEditable(list)) return;
        ModifierRandomizer.Settings.SetRange(row.Group, min, max);
        ModifierRandomizer.Save();
        Refresh(list);
    }

    internal static void Refresh(NCustomRunModifiersList list)
    {
        if (!States.TryGetValue(list, out var state)) return;
        var available = Candidates(list, ignoreLocks: false);
        foreach (var row in state.Ranges)
        {
            row.Applying = true;
            try
            {
                var (min, max) = ModifierRandomizer.Settings.Range(row.Group);
                min = Math.Min(min, row.Total);
                max = Math.Min(max, row.Total);
                // Co-op in singleplayer, or a group emptied by Blitz, has nothing to roll.
                row.Root.Visible = available.TryGetValue(row.Group, out var pool) && pool.Count > 0;
                row.Value.Text = min == max ? min.ToString() : $"{min}–{max}";
                row.Slider.SetValues(min, max, notify: false);
            }
            finally { row.Applying = false; }
        }
    }

    private static Dictionary<ModifierGroup, IReadOnlyList<ModifierModel>> Candidates(NCustomRunModifiersList list, bool ignoreLocks)
    {
        var negatives = ModelDb.BadModifiers.Select(modifier => modifier.GetType()).ToHashSet();
        var blitz = !ignoreLocks && BlitzCompatibility.IsBlitz(list);
        return Tickboxes(list).Select(row => row.Modifier).OfType<ModifierModel>()
            .Where(modifier => !ModifierVariantUi.IsHiddenVariant(modifier) && modifier is not SubmodifierModel)
            .Where(modifier => ignoreLocks || !ModifierGroupsUi.IsLocked(list, modifier))
            .Where(modifier => !(blitz && BlitzCompatibility.IsForced(modifier)))
            .Where(modifier => ignoreLocks || AdvancedMode.IsActive(list) || !AdvancedMode.IsAdvancedModifier(modifier))
            .GroupBy(modifier => ModifierGroups.Classify(modifier, negatives))
            .ToDictionary(group => group.Key, group => (IReadOnlyList<ModifierModel>)group.ToList());
    }

    private static void Roll(NCustomRunModifiersList list, bool chaos)
    {
        if (!GodotObject.IsInstanceValid(list) || !IsEditable(list)) return;
        try
        {
            var rng = Random.Shared;
            var settings = ModifierRandomizer.Settings;
            var candidates = Candidates(list, ignoreLocks: false);
            if (chaos)
            {
                foreach (var group in ModifierRandomizer.RollGroups)
                {
                    if (!candidates.TryGetValue(group, out var pool) || pool.Count == 0) continue;
                    var (min, max) = ModifierRandomizer.RandomRange(pool.Count, rng);
                    settings.SetRange(group, min, max);
                }
                ModifierRandomizer.Save();
                Refresh(list);
            }

            var blitz = BlitzCompatibility.IsBlitz(list);
            var advanced = AdvancedMode.IsActive(list);
            var tickboxes = Tickboxes(list);
            // Each picked modifier remembers its checkbox position, so the result keeps the native list order.
            var selected = new List<(int Row, ModifierModel Modifier)>();
            int RowOf(ModifierModel? modifier) => tickboxes.FindIndex(row => row.Modifier == modifier);
            foreach (var source in ModifierRandomizer.Pick(candidates, settings.Range, rng))
            {
                var chosen = source;
                if (ModifierVariantUi.Variants(list, source) is { } variants)
                    chosen = settings.Values ? variants.All[rng.Next(variants.All.Count)] : variants.Current;
                var modifier = (ModifierModel)chosen.MutableClone();
                // Slider values stay at their defaults unless Advanced mode shows them.
                if (settings.Values && advanced) ModifierRandomizer.RandomizeValues(modifier, rng, blitz);
                selected.Add((RowOf(source), modifier));
                foreach (var child in tickboxes.Where(row => row.Modifier is SubmodifierModel sub && sub.ParentType == source.GetType()))
                    if (settings.Values ? rng.Next(2) == 0 : child.IsTicked)
                        selected.Add((RowOf(child.Modifier), (ModifierModel)child.Modifier!.MutableClone()));
            }
            // Blitz keeps its always-on modifiers with their current values.
            if (blitz)
                selected.AddRange(tickboxes.Select(row => row.Modifier).OfType<ModifierModel>()
                    .Where(BlitzCompatibility.IsForced).Select(modifier => (RowOf(modifier), (ModifierModel)modifier.MutableClone())));

            // A shuffled list would also shuffle the Neow start order, which presets and syncs read from list order.
            list.SetTickedModifiers(selected.OrderBy(entry => entry.Row).Select(entry => entry.Modifier).ToList());
        }
        catch (Exception ex) { GD.PushError($"[Ultimate Custom Run] Could not randomize the custom run: {ex}"); }
    }
}
