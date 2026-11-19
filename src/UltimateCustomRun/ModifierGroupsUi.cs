using System.Runtime.CompilerServices;
using OpCodes = System.Reflection.Emit.OpCodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.Core.Models;

namespace UltimateCustomRun;

// Modifier groups are shown one at a time and picked from a column of native settings tabs beside the list.
[HarmonyPatch(typeof(NCustomRunModifiersList), nameof(NCustomRunModifiersList._Ready))]
internal static class ModifierGroupsUi
{
    private sealed class Section
    {
        internal required ModifierGroup Group;
        internal required VBoxContainer Root;
        internal required NSettingsTab Tab;
        internal required Control Badge;
        internal required Label BadgeLabel;
        internal required MarginContainer Body;
        internal required VBoxContainer ChildList;
        internal required List<NRunModifierTickbox> Rows;
        internal bool Available = true;
    }
    private sealed class LockEntry
    {
        internal required NRunModifierTickbox Row;
        internal required List<Control> Nodes;
        internal Section Home = null!;
    }
    private sealed class Layout
    {
        internal required NCustomRunModifiersList List;
        internal required VBoxContainer Content;
        internal required VBoxContainer Rail;
        internal NRunModifierTickbox? AdvancedToggle;
        internal List<Section> Sections = [];
        internal List<LockEntry> Lockable = [];
        internal Dictionary<Section, List<Node>> OriginalChildren = [];
        internal Dictionary<Section, List<NRunModifierTickbox>> OriginalRows = [];
        // Forced rows and their tick state from before the game mode forced them.
        internal Dictionary<NRunModifierTickbox, bool> Forced = [];
        internal ModifierGroup Selected = SelectedGroup;
    }
    private static readonly ConditionalWeakTable<NCustomRunModifiersList, Layout> Layouts = new();
    private static readonly Color BadgeColor = new(0.333333f, 0.580392f, 0.647059f);
    private static readonly Color Cream = new(1, 0.9647059f, 0.8862745f);
    private const string TabScene = "res://scenes/screens/settings_tab.tscn";
    private const float TabWidth = 176;
    private const float TabHeight = 60;
    // Remembered for the session, so reopening the screen returns to the last tab.
    private static ModifierGroup SelectedGroup = ModifierGroup.ImprovedStart;

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var source = AccessTools.Method(typeof(NCustomRunModifiersList), "GetAllModifiers");
        for (var i = 0; i < code.Count; i++)
        {
            if (!code[i].Calls(source)) continue;
            // Inject at the actual UI enumeration call site; a tiny iterator factory may be inlined.
            code.Insert(i + 1, new CodeInstruction(OpCodes.Call,
                AccessTools.Method(typeof(ModifierListPatch), nameof(ModifierListPatch.ForCustomRun))));
            return code;
        }
        throw new InvalidOperationException("Custom Run modifier enumeration changed; could not add our custom modifiers safely.");
    }

    [HarmonyPostfix]
    private static void Postfix(NCustomRunModifiersList __instance)
    {
        // Wait for vanilla's queued child additions/removals and each checkbox's _Ready.
        Callable.From(() => Build(__instance)).CallDeferred();
    }

    internal static Control? DefaultFocus(NCustomRunModifiersList list)
    {
        if (!Layouts.TryGetValue(list, out var layout)) return null;
        return ContentControls(layout).FirstOrDefault() ?? SelectedSection(layout)?.Tab;
    }

    internal static string Title(ModifierGroup group)
    {
        var russian = LocManager.Instance.CultureInfo.TwoLetterISOLanguageName == "ru";
        if (russian)
            return group switch
            {
                ModifierGroup.Randomizer => "Случайно",
                ModifierGroup.ImprovedStart => "Старт",
                ModifierGroup.RunParameters => "Параметры",
                ModifierGroup.Modifiers => "Модификаторы",
                ModifierGroup.CardPool => "Пул карт",
                ModifierGroup.Ascension => "Возвышение",
                ModifierGroup.Negatives => "Негативные",
                ModifierGroup.Coop => "Кооператив",
                ModifierGroup.Disabled => "Отключено",
                _ => group.ToString()
            };
        return group switch
        {
            ModifierGroup.ImprovedStart => "Start",
            ModifierGroup.RunParameters => "Parameters",
            _ => ModifierGroups.Sections.First(section => section.Group == group).Title
        };
    }

    private static void Build(NCustomRunModifiersList list)
    {
        if (!GodotObject.IsInstanceValid(list) || Layouts.TryGetValue(list, out _)) return;
        var content = list.GetNode<VBoxContainer>("ScrollContainer/Mask/Content");
        var rows = (List<NRunModifierTickbox>)AccessTools.Field(typeof(NCustomRunModifiersList), "_modifierTickboxes").GetValue(list)!;
        var negatives = ModelDb.BadModifiers.Select(m => m.GetType()).ToHashSet();
        var rail = CreateRail(list);
        var layout = new Layout { List = list, Content = content, Rail = rail };
        Layouts.Add(list, layout);
        content.AddThemeConstantOverride("separation", 12);
        layout.AdvancedToggle = CreateAdvancedToggle(list, content);

        foreach (var (group, _) in ModifierGroups.Sections)
        {
            var children = rows.Where(row => row.Modifier != null && ModifierGroups.Classify(row.Modifier, negatives) == group).ToList();
            var visibleRows = children.Where(row => !ModifierVariantUi.IsHiddenVariant(row.Modifier) &&
                row.Modifier is not SubmodifierModel && !DeckPickerUi.IsDeckModifier(row.Modifier)).ToList();
            var sectionRoot = new VBoxContainer { Name = group.ToString(), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Visible = false };
            if (group == ModifierGroup.Disabled) sectionRoot.Modulate = new Color(1, 1, 1, 0.6f);
            sectionRoot.AddThemeConstantOverride("separation", 4);
            var body = new MarginContainer { Name = "Children", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            body.AddThemeConstantOverride("margin_left", 0);
            var childList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            childList.AddThemeConstantOverride("separation", 8);
            body.AddChild(childList);
            if (group == ModifierGroup.Disabled)
                body.AddChild(new Control { Name = "ClickBlocker", MouseFilter = Control.MouseFilterEnum.Stop });
            sectionRoot.AddChild(body);
            content.AddChild(sectionRoot);
            if (group == ModifierGroup.Randomizer) ModifierRandomizerUi.Create(list, childList);
            var variantRows = new List<ModifierVariantUi.VariantRow>();
            try
            {
                foreach (var family in ModifierVariantUi.Families)
                {
                    var parentRow = children.FirstOrDefault(row => ModifierVariantUi.IsParent(row.Modifier, family));
                    var draftRow = children.FirstOrDefault(row => ModifierVariantUi.IsDraft(row.Modifier, family));
                    var pickAnyRow = children.FirstOrDefault(row => ModifierVariantUi.IsPickAny(row.Modifier, family));
                    if (parentRow == null || draftRow == null) continue;
                    variantRows.Add(ModifierVariantUi.Create(list, family, parentRow, draftRow, pickAnyRow, () => Refresh(list)));
                }
            }
            catch (Exception ex)
            {
                GD.PushError($"[Ultimate Custom Run] Could not build modifier sub-options: {ex}");
            }
            var attachments = new Dictionary<NRunModifierTickbox, List<Control>>();
            foreach (var row in children)
            {
                row.Reparent(childList);
                row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                var attached = attachments[row] = [];
                var lockEntry = ModifierGroups.IsSingleplayerDisabled(row.Modifier) || BlitzCompatibility.IsDisabled(row.Modifier)
                    ? new LockEntry { Row = row, Nodes = [row] } : null;
                if (lockEntry != null) layout.Lockable.Add(lockEntry);
                if (ModifierVariantUi.IsHiddenVariant(row.Modifier))
                {
                    row.Visible = false;
                    row.FocusMode = Control.FocusModeEnum.None;
                    continue;
                }
                // Custom-only modifiers are absent from the global daily pool.
                if (row.Modifier is { } modifier &&
                    (ModifierListPatch.IsCustomOnly(modifier) || ModifierVariantUi.IsParent(modifier)))
                {
                    var text = new LocString("main_menu_ui", "CUSTOM_RUN_SCREEN.MODIFIER_LABEL");
                    var modifierTitle = modifier.Title.GetFormattedText();
                    if (ModifierVariantUi.IsParent(modifier)) modifierTitle += " [color=#ff9c3d]*[/color]";
                    text.Add("color", group is ModifierGroup.Ascension or ModifierGroup.Negatives ? "red" : "green");
                    text.Add("modifier_title", modifierTitle);
                    text.Add("modifier_description", modifier.Description.GetFormattedText());
                    row.GetNode<MegaCrit.Sts2.addons.mega_text.MegaRichTextLabel>("HBoxContainer/Description").Text = text.GetFormattedText();
                }
                foreach (var variantRow in variantRows.Where(variantRow => ReferenceEquals(row, variantRow.ParentRow)))
                {
                    childList.AddChild(variantRow.Container);
                    attached.Add(variantRow.Container);
                    lockEntry?.Nodes.Add(variantRow.Container);
                }
                if (ModifierValueUi.Create(list, row) is { } valueRow)
                {
                    childList.AddChild(valueRow);
                    attached.Add(valueRow);
                    lockEntry?.Nodes.Add(valueRow);
                }
                if (CustomRunParametersUi.Create(list, row) is { } parameterRows)
                {
                    childList.AddChild(parameterRows);
                    attached.Add(parameterRows);
                }
                if (CoopHandicapUi.Create(list, row) is { } handicapRows)
                {
                    childList.AddChild(handicapRows);
                    attached.Add(handicapRows);
                    lockEntry?.Nodes.Add(handicapRows);
                }
            }
            if (group == ModifierGroup.ImprovedStart)
            {
                try { DeckPickerUi.Create(list, childList, children, attachments, () => Refresh(list)); }
                catch (Exception ex) { GD.PushError($"[Ultimate Custom Run] Could not build the starting deck picker: {ex}"); }
                childList.AddChild(StartOrderUi.Create(list));
            }
            if (variantRows.Count > 0) ModifierVariantUi.InitializeChoices(list);
            LinkedModifierChains.Attach(body, visibleRows);
            var (tab, badge, badgeLabel) = CreateTab(rail, Title(group));
            var section = new Section
            {
                Group = group, Root = sectionRoot, Tab = tab, Badge = badge, BadgeLabel = badgeLabel,
                Body = body, ChildList = childList, Rows = visibleRows
            };
            layout.Sections.Add(section);
            tab.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => Select(layout, section.Group, true)));
        }

        SubmodifierUi.Attach(list, rows);
        foreach (var section in layout.Sections)
        {
            layout.OriginalChildren[section] = [.. section.ChildList.GetChildren()];
            layout.OriginalRows[section] = [.. section.Rows];
        }
        foreach (var entry in layout.Lockable)
            entry.Home = layout.Sections.First(section => section.ChildList == entry.Row.GetParent());
        content.MinimumSizeChanged += () => Callable.From(() => ResizeContent(content)).CallDeferred();
        list.ModifiersChanged += () => RefreshSummary(layout);
        ActiveModifiersUi.Create(list, modifier => Reveal(list, modifier));
        ApplyMode(list, layout);
        DeckPickerUi.Refresh(list);
        ModifierValueUi.Refresh(list);
        UpdateFocus(layout);
        ResizeContent(content);
        Callable.From(() => ResizeContent(content)).CallDeferred();
    }

    private static VBoxContainer CreateRail(NCustomRunModifiersList list)
    {
        var rail = new VBoxContainer { Name = "ModifierTabs", MouseFilter = Control.MouseFilterEnum.Ignore };
        rail.AddThemeConstantOverride("separation", 4);
        // The list is anchored to the right container with a wide right margin; the tabs fill that margin.
        var parent = list.GetParent<Control>();
        parent.AddChild(rail);
        rail.AnchorLeft = rail.AnchorRight = 1;
        rail.AnchorTop = rail.AnchorBottom = 0;
        rail.OffsetLeft = -TabWidth - 16;
        rail.OffsetRight = -16;
        rail.OffsetTop = list.OffsetTop;
        rail.OffsetBottom = list.OffsetTop + 10 * (TabHeight + 4);
        return rail;
    }

    private static (NSettingsTab Tab, Control Badge, Label BadgeLabel) CreateTab(VBoxContainer rail, string title)
    {
        var tab = ResourceLoader.Load<PackedScene>(TabScene).Instantiate<NSettingsTab>();
        tab.Name = "Tab";
        tab.CustomMinimumSize = new Vector2(TabWidth, TabHeight);
        tab.PivotOffset = new Vector2(TabWidth / 2, TabHeight / 2);
        tab.FocusMode = Control.FocusModeEnum.All;
        rail.AddChild(tab);
        tab.SetLabel(title);

        // Count of selected modifiers, drawn on the card library's cost tickbox like a native filter chip.
        var badge = new Control
        {
            Name = "Badge", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false,
            CustomMinimumSize = new Vector2(34, 34)
        };
        tab.AddChild(badge);
        badge.AnchorLeft = badge.AnchorRight = 1;
        badge.AnchorTop = badge.AnchorBottom = 0;
        badge.OffsetLeft = -30; badge.OffsetRight = 4; badge.OffsetTop = -6; badge.OffsetBottom = 28;
        var image = new TextureRect
        {
            Texture = ResourceLoader.Load<Texture2D>("res://images/packed/card_library/cost_tickbox.png"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SelfModulate = BadgeColor, MouseFilter = Control.MouseFilterEnum.Ignore
        };
        badge.AddChild(image);
        image.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var label = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeFontOverride("font", ResourceLoader.Load<Font>("res://themes/kreon_bold_shared.tres"));
        label.AddThemeFontSizeOverride("font_size", 20);
        label.AddThemeColorOverride("font_color", Cream);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.5f));
        label.AddThemeConstantOverride("outline_size", 8);
        badge.AddChild(label);
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return (tab, badge, label);
    }

    private static NRunModifierTickbox CreateAdvancedToggle(NCustomRunModifiersList list, VBoxContainer content)
    {
        var russian = LocManager.Instance.CultureInfo.TwoLetterISOLanguageName == "ru";
        return ModifierRandomizerUi.AddOption(list, content, "AdvancedModeToggle",
            russian ? "Расширенный режим" : "Advanced mode",
            russian ? "Параметры забега и ползунки значений." : "Run Parameters and value sliders.",
            () => AdvancedMode.Enabled, value => AdvancedMode.SetEnabled(list, value));
    }

    internal static void RefreshAdvancedToggle(NCustomRunModifiersList list)
    {
        if (!GodotObject.IsInstanceValid(list) || !Layouts.TryGetValue(list, out var layout) ||
            layout.AdvancedToggle is not { } toggle || !GodotObject.IsInstanceValid(toggle)) return;
        // Clients follow the host's selection and cannot change the run, so the option is the host's alone.
        toggle.Visible = IsEditable(list);
        toggle.FocusMode = toggle.Visible ? Control.FocusModeEnum.All : Control.FocusModeEnum.None;
        if (toggle.IsNodeReady()) toggle.IsTicked = AdvancedMode.Enabled;
    }

    internal static void ApplyMode(NCustomRunModifiersList list)
    {
        if (!GodotObject.IsInstanceValid(list) || !Layouts.TryGetValue(list, out var layout)) return;
        ApplyMode(list, layout);
        Refresh(list);
    }

    private static void ApplyMode(NCustomRunModifiersList list, Layout layout)
    {
        // Only the player who sets the modifiers can roll them.
        if (layout.Sections.FirstOrDefault(section => section.Group == ModifierGroup.Randomizer) is { } randomizer)
        {
            randomizer.Available = IsEditable(list);
            ModifierRandomizerUi.Refresh(list);
        }
        var disabled = layout.Sections.FirstOrDefault(section => section.Group == ModifierGroup.Disabled);
        if (disabled == null) return;
        var moved = false;
        var unticked = false;
        foreach (var entry in layout.Lockable)
        {
            var row = entry.Row;
            var locked = IsLocked(list, row.Modifier);
            var target = locked ? disabled : entry.Home;
            if (row.GetParent() != target.ChildList)
            {
                var source = target == disabled ? entry.Home : disabled;
                var focus = layout.Content.GetViewport()?.GuiGetFocusOwner();
                if (focus != null && entry.Nodes.Any(node => node == focus || node.IsAncestorOf(focus)))
                    source.Tab.GrabFocus();
                foreach (var node in entry.Nodes) node.Reparent(target.ChildList, false);
                moved = true;
            }
            if (locked && row.IsTicked) { row.IsTicked = false; unticked = true; }
            row.FocusMode = locked || ModifierVariantUi.IsHiddenVariant(row.Modifier)
                ? Control.FocusModeEnum.None
                : Control.FocusModeEnum.All;
        }
        if (moved)
            foreach (var section in layout.Sections) Reorder(layout, section);
        var forcedChanged = ApplyForced(list, layout);
        if (unticked || forcedChanged) ModifierVariantUi.RefreshFromTickboxes(list);
        ApplyAdvanced(list, layout);
        // A group whose modifiers all moved to Disabled (or Disabled itself when empty) hides its tab.
        foreach (var section in layout.Sections.Where(section => section.Group != ModifierGroup.Randomizer))
            section.Available = section.Rows.Any(row => row.Visible);
        ApplySelection(layout);
        RefreshSummary(layout);
    }

    // Without Advanced mode, slider-only modifiers disappear entirely; their extra rows follow because they are unticked.
    private static void ApplyAdvanced(NCustomRunModifiersList list, Layout layout)
    {
        RefreshAdvancedToggle(list);
        var advanced = AdvancedMode.IsActive(list);
        var focus = layout.Content.GetViewport()?.GuiGetFocusOwner();
        foreach (var section in layout.Sections)
            foreach (var row in section.Rows.Where(row => AdvancedMode.IsAdvancedModifier(row.Modifier)))
            {
                if (!advanced && focus != null && (row == focus || row.IsAncestorOf(focus))) section.Tab.GrabFocus();
                row.Visible = advanced;
                row.FocusMode = advanced && !IsLocked(list, row.Modifier) ? Control.FocusModeEnum.All : Control.FocusModeEnum.None;
            }
    }

    // Lockable nodes return to their original positions; nodes moved in from other sections follow in build order.
    private static void Reorder(Layout layout, Section section)
    {
        var original = layout.OriginalChildren.GetValueOrDefault(section) ?? [];
        var current = section.ChildList.GetChildren().ToHashSet();
        var incoming = layout.Lockable.Where(entry => entry.Home != section)
            .SelectMany(entry => entry.Nodes).Where(current.Contains);
        var order = original.Where(current.Contains).Concat(incoming).Distinct().ToList();
        for (var i = 0; i < order.Count; i++) section.ChildList.MoveChild(order[i], i);
        var rows = order.OfType<NRunModifierTickbox>().ToHashSet();
        var originalRows = layout.OriginalRows.GetValueOrDefault(section) ?? [];
        section.Rows = originalRows.Where(rows.Contains)
            .Concat(layout.Lockable.Where(entry => entry.Home != section && rows.Contains(entry.Row) &&
                !ModifierVariantUi.IsHiddenVariant(entry.Row.Modifier)).Select(entry => entry.Row))
            .Distinct().ToList();
    }

    // In Blitz, Blitz Merchant is always on: the mode already uses it for every Merchant, and its slider sets the picks.
    private static bool ApplyForced(NCustomRunModifiersList list, Layout layout)
    {
        var editable = IsEditable(list);
        var blitz = BlitzCompatibility.IsBlitz(list);
        var changed = false;
        var forcedRows = layout.Sections.SelectMany(section => section.Rows)
            .Where(row => BlitzCompatibility.IsForced(row.Modifier)).ToList();
        foreach (var row in forcedRows)
        {
            if (blitz)
            {
                if (!layout.Forced.ContainsKey(row)) layout.Forced[row] = row.IsTicked;
                // Clients mirror the host selection, which already contains the forced modifier.
                if (editable && !row.IsTicked) { row.IsTicked = true; changed = true; }
                row.MouseFilter = Control.MouseFilterEnum.Ignore;
                row.FocusMode = Control.FocusModeEnum.None;
            }
            else if (layout.Forced.Remove(row, out var previous))
            {
                if (editable && row.IsTicked != previous) { row.IsTicked = previous; changed = true; }
                row.MouseFilter = Control.MouseFilterEnum.Stop;
                row.FocusMode = row.IsEnabled ? Control.FocusModeEnum.All : Control.FocusModeEnum.None;
            }
        }
        return changed;
    }

    internal static bool IsSingleplayer(NCustomRunModifiersList list) =>
        (MultiplayerUiMode)AccessTools.Field(typeof(NCustomRunModifiersList), "_mode").GetValue(list)! == MultiplayerUiMode.Singleplayer;

    internal static bool IsEditable(NCustomRunModifiersList list) =>
        (MultiplayerUiMode)AccessTools.Field(typeof(NCustomRunModifiersList), "_mode").GetValue(list)!
            is MultiplayerUiMode.Singleplayer or MultiplayerUiMode.Host;

    internal static bool IsLocked(NCustomRunModifiersList list, ModifierModel? modifier) =>
        (ModifierGroups.IsSingleplayerDisabled(modifier) && IsSingleplayer(list)) ||
        (BlitzCompatibility.IsDisabled(modifier) && BlitzCompatibility.IsBlitz(list));

    internal static void RemoveLockedModifiers(NCustomRunModifiersList list, List<ModifierModel> modifiers)
    {
        modifiers.RemoveAll(modifier => IsLocked(list, modifier));
        if (!BlitzCompatibility.IsBlitz(list)) return;
        var tickboxes = (List<NRunModifierTickbox>)AccessTools.Field(typeof(NCustomRunModifiersList), "_modifierTickboxes").GetValue(list)!;
        foreach (var forced in tickboxes.Select(row => row.Modifier).OfType<ModifierModel>().Where(BlitzCompatibility.IsForced))
            if (!modifiers.Any(modifier => modifier.GetType() == forced.GetType())) modifiers.Add(forced);
    }

    private static Section? SelectedSection(Layout layout) =>
        layout.Sections.FirstOrDefault(section => section.Group == layout.Selected && section.Available);

    private static void Select(Layout layout, ModifierGroup group, bool userChoice)
    {
        var focus = layout.Content.GetViewport()?.GuiGetFocusOwner();
        var previous = SelectedSection(layout);
        layout.Selected = group;
        if (userChoice) SelectedGroup = group;
        ApplySelection(layout);
        if (previous != null && previous.Group != group)
        {
            try { layout.List.GetNodeOrNull<NScrollableContainer>("ScrollContainer")?.InstantlyScrollToTop(); }
            catch (InvalidOperationException) { }
            // Keep controller focus on the tab column instead of a control that was just hidden.
            if (focus != null && previous.Root.IsAncestorOf(focus)) SelectedSection(layout)?.Tab.GrabFocus();
        }
        Refresh(layout.List);
    }

    private static void ApplySelection(Layout layout)
    {
        if (SelectedSection(layout) == null)
        {
            var fallback = layout.Sections.FirstOrDefault(section => section.Available && section.Group == ModifierGroup.ImprovedStart)
                ?? layout.Sections.FirstOrDefault(section => section.Available && section.Group != ModifierGroup.Randomizer)
                ?? layout.Sections.FirstOrDefault(section => section.Available);
            if (fallback != null) layout.Selected = fallback.Group;
        }
        foreach (var section in layout.Sections)
        {
            var selected = section.Available && section.Group == layout.Selected;
            section.Root.Visible = selected;
            section.Tab.Visible = section.Available;
            section.Tab.FocusMode = section.Available ? Control.FocusModeEnum.All : Control.FocusModeEnum.None;
            if (!section.Tab.IsNodeReady()) continue;
            if (selected) section.Tab.Select();
            else section.Tab.Deselect();
        }
    }

    // Opens the tab that holds a selected modifier and brings its row into view.
    private static void Reveal(NCustomRunModifiersList list, ModifierModel modifier)
    {
        if (!GodotObject.IsInstanceValid(list) || !Layouts.TryGetValue(list, out var layout)) return;
        var tickboxes = (List<NRunModifierTickbox>)AccessTools.Field(typeof(NCustomRunModifiersList), "_modifierTickboxes").GetValue(list)!;
        var row = tickboxes.FirstOrDefault(row => ReferenceEquals(row.Modifier, modifier))
            ?? tickboxes.FirstOrDefault(row => row.Modifier?.GetType() == modifier.GetType());
        if (row == null) return;
        var section = layout.Sections.FirstOrDefault(section => section.ChildList.IsAncestorOf(row));
        if (section == null || !section.Available) return;
        Select(layout, section.Group, true);
        // Hidden rows (variants, starting deck) are represented by the visible control that drives them.
        Control? target = row.Visible ? row
            : DeckPickerUi.IsDeckModifier(modifier) ? section.ChildList.GetNodeOrNull<Control>(DeckPickerUi.NodeName)
            : tickboxes.FirstOrDefault(parent => parent.Visible && parent.GetParent() == row.GetParent() &&
                ModifierVariantUi.Families.Any(family => ModifierVariantUi.IsParent(parent.Modifier, family) &&
                    ModifierVariantUi.BelongsTo(modifier, family)));
        if (target == null) return;
        // Wait one frame so the newly shown tab has a layout to scroll through.
        Callable.From(() => ScrollTo(layout, target)).CallDeferred();
    }

    private static void ScrollTo(Layout layout, Control target)
    {
        if (!GodotObject.IsInstanceValid(target) || !target.IsVisibleInTree() || !GodotObject.IsInstanceValid(layout.Content)) return;
        var scroll = layout.List.GetNodeOrNull<NScrollableContainer>("ScrollContainer");
        if (scroll == null) return;
        try
        {
            // The scroll container eases toward this private target each frame; it only follows focus for controllers.
            var limit = (float)AccessTools.Property(typeof(NScrollableContainer), "ScrollLimitBottom").GetValue(scroll)!;
            var value = layout.Content.Position.Y + (scroll.GlobalPosition.Y - target.GlobalPosition.Y) + 24;
            value = Mathf.Clamp(value, Mathf.Min(limit, 0), Mathf.Max(limit, 0));
            AccessTools.Field(typeof(NScrollableContainer), "_targetDragPosY").SetValue(scroll, value);
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[Ultimate Custom Run] Could not scroll to the selected modifier: {ex.Message}");
        }
    }

    private static void RefreshBadges(Layout layout)
    {
        foreach (var section in layout.Sections)
        {
            if (!GodotObject.IsInstanceValid(section.Badge)) continue;
            var count = section.Group is ModifierGroup.Randomizer or ModifierGroup.Disabled ? 0 :
                section.ChildList.GetChildren().OfType<NRunModifierTickbox>().Count(row =>
                    row.IsTicked && row.Modifier != null && !ModifierVariantUi.IsHiddenVariant(row.Modifier));
            section.Badge.Visible = count > 0;
            section.BadgeLabel.Text = count.ToString();
        }
    }

    private static void RefreshSummary(Layout layout)
    {
        if (!GodotObject.IsInstanceValid(layout.List)) return;
        RefreshBadges(layout);
        ActiveModifiersUi.Refresh(layout.List);
    }

    private static void ResizeContent(VBoxContainer content)
    {
        if (GodotObject.IsInstanceValid(content))
            content.Size = new Vector2(content.Size.X, content.GetCombinedMinimumSize().Y);
    }

    internal static void Refresh(NCustomRunModifiersList list)
    {
        if (!GodotObject.IsInstanceValid(list) || !Layouts.TryGetValue(list, out var layout)) return;
        UpdateFocus(layout);
        ResizeContent(layout.Content);
        Callable.From(() => ResizeContent(layout.Content)).CallDeferred();
    }

    private static List<Control> ContentControls(Layout layout)
    {
        var controls = SelectedSection(layout) is { } section ? FocusableControls(section).ToList() : [];
        if (layout.AdvancedToggle is { Visible: true } toggle && toggle.FocusMode == Control.FocusModeEnum.All) controls.Insert(0, toggle);
        return controls;
    }

    // Up and down move within the open tab; right jumps to the tab column and left returns to the list.
    private static void UpdateFocus(Layout layout)
    {
        var controls = ContentControls(layout);
        var tabs = layout.Sections.Where(section => section.Available).Select(section => (Control)section.Tab).ToList();
        var selectedTab = SelectedSection(layout)?.Tab;
        for (var i = 0; i < controls.Count; i++)
        {
            var previous = controls[(i + controls.Count - 1) % controls.Count];
            var next = controls[(i + 1) % controls.Count];
            controls[i].FocusNeighborTop = controls[i].GetPathTo(previous);
            controls[i].FocusPrevious = controls[i].GetPathTo(previous);
            controls[i].FocusNeighborBottom = controls[i].GetPathTo(next);
            controls[i].FocusNext = controls[i].GetPathTo(next);
            if (selectedTab != null && controls[i] is not Godot.Range) controls[i].FocusNeighborRight = controls[i].GetPathTo(selectedTab);
        }
        for (var i = 0; i < tabs.Count; i++)
        {
            var previous = tabs[Math.Max(0, i - 1)];
            var next = tabs[Math.Min(tabs.Count - 1, i + 1)];
            tabs[i].FocusNeighborTop = tabs[i].GetPathTo(previous);
            tabs[i].FocusNeighborBottom = tabs[i].GetPathTo(next);
            tabs[i].FocusPrevious = tabs[i].GetPathTo(previous);
            tabs[i].FocusNext = tabs[i].GetPathTo(next);
            tabs[i].FocusNeighborRight = tabs[i].GetPathTo(tabs[i]);
            tabs[i].FocusNeighborLeft = controls.Count > 0 ? tabs[i].GetPathTo(controls[0]) : tabs[i].GetPathTo(tabs[i]);
        }
    }

    private static IEnumerable<Control> FocusableControls(Section section)
    {
        if (!section.Root.Visible || section.Group == ModifierGroup.Disabled) yield break;
        foreach (var child in section.ChildList.GetChildren().OfType<Control>())
        {
            if (!child.Visible) continue;
            if (child is NRunModifierTickbox row && row.FocusMode == Control.FocusModeEnum.All) yield return row;
            foreach (var nested in child.FindChildren("*", "Control", true, false).OfType<NRunModifierTickbox>())
                if (nested.IsVisibleInTree() && nested.FocusMode == Control.FocusModeEnum.All) yield return nested;
            foreach (var slider in child.FindChildren("*", "Range", true, false).OfType<Control>())
                if (slider.FocusMode == Control.FocusModeEnum.All) yield return slider;
            if (section.Group == ModifierGroup.Randomizer)
                foreach (var control in child.FindChildren("*", "Control", true, false).OfType<Control>())
                    if ((control is NButton || control.HasMeta(RangeSlider.FocusMeta)) && control.FocusMode == Control.FocusModeEnum.All)
                        yield return control;
            foreach (var button in StartOrderUi.FocusableControls(child)) yield return button;
        }
    }
}

[HarmonyPatch(typeof(NCustomRunModifiersList), nameof(NCustomRunModifiersList.DefaultFocusedControl), MethodType.Getter)]
internal static class ModifierGroupsFocusPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCustomRunModifiersList __instance, ref Control? __result) =>
        __result = ModifierGroupsUi.DefaultFocus(__instance) ?? __result;
}
