using System.Runtime.CompilerServices;
using OpCodes = System.Reflection.Emit.OpCodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Models;

namespace UltimateCustomRun;

// Modifier groups are shown one at a time inside a dark panel, picked from a column of tabs on its left.
[HarmonyPatch(typeof(NCustomRunModifiersList), nameof(NCustomRunModifiersList._Ready))]
internal static class ModifierGroupsUi
{
    private sealed class Section
    {
        internal required ModifierGroup Group;
        internal required VBoxContainer Root;
        internal required NButton Tab;
        internal required TabVisual Visual;
        internal required MarginContainer Body;
        internal required VBoxContainer ChildList;
        internal required List<NRunModifierTickbox> Rows;
        internal bool Available = true;
    }
    private sealed class TabVisual
    {
        internal required Control Banner;
        internal required ShaderMaterial Material;
        internal required Label Label;
        internal required Control Badge;
        internal required Label BadgeLabel;
        internal bool Selected;
        internal bool Hovered;
        internal bool Disabled;
        internal Tween? Tween;
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
    // The modifier panel spans from just right of the left column to the scrollbar; tabs run down its left edge.
    private const float PanelLeft = -70;
    private const float PanelPadding = 10;
    private const float TabWidth = 172;
    private const float TabHeight = 50;
    private const float ScrollbarWidth = 52;
    private const float TopGap = 16;
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
        // The native scene grows Content both ways, so anything too wide pushed the checkboxes under the left mask edge.
        content.GrowHorizontal = Control.GrowDirection.End;
        var rows = (List<NRunModifierTickbox>)AccessTools.Field(typeof(NCustomRunModifiersList), "_modifierTickboxes").GetValue(list)!;
        var negatives = ModelDb.BadModifiers.Select(m => m.GetType()).ToHashSet();
        var rail = CreateRail(list);
        var layout = new Layout { List = list, Content = content, Rail = rail };
        Layouts.Add(list, layout);
        content.AddThemeConstantOverride("separation", 12);
        layout.AdvancedToggle = CreateAdvancedToggle(list, rail);

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
                if (!DeckPickerUi.IsDeckModifier(row.Modifier))
                    CompactRowsUi.Attach(row, group is ModifierGroup.Ascension or ModifierGroup.Negatives);
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
            var (tab, visual) = CreateTab(rail, Title(group));
            var section = new Section
            {
                Group = group, Root = sectionRoot, Tab = tab, Visual = visual,
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
        var parent = list.GetParent<Control>();
        var top = list.OffsetTop;
        var bottom = list.OffsetBottom;
        var listRight = list.OffsetRight;

        var panel = VanillaUi.Box(new Color(0, 0, 0, 0.5f));
        panel.Name = "ModifierPanel";
        parent.AddChild(panel);
        parent.MoveChild(panel, list.GetIndex());
        panel.AnchorLeft = panel.AnchorTop = 0;
        panel.AnchorRight = panel.AnchorBottom = 1;
        panel.OffsetLeft = PanelLeft;
        panel.OffsetTop = top + TopGap - PanelPadding;
        panel.OffsetRight = listRight + ScrollbarWidth + PanelPadding;
        panel.OffsetBottom = bottom + PanelPadding;

        var divider = new ColorRect { Color = new Color(VanillaUi.Cream, 0.12f), MouseFilter = Control.MouseFilterEnum.Ignore };
        parent.AddChild(divider);
        parent.MoveChild(divider, list.GetIndex());
        divider.AnchorLeft = divider.AnchorRight = divider.AnchorTop = 0;
        divider.AnchorBottom = 1;
        divider.OffsetLeft = PanelLeft + PanelPadding + TabWidth + 8;
        divider.OffsetRight = divider.OffsetLeft + 2;
        divider.OffsetTop = top + TopGap + 4;
        divider.OffsetBottom = bottom - 4;

        // The list keeps its right edge and starts after the tab column.
        list.OffsetLeft = divider.OffsetRight + 14;
        // Leave room below the loadout dropdown, which hangs a little below the title row.
        list.OffsetTop = top + TopGap;
        // Align the Modifiers title, loadout picker and Save button with the panel edge.
        if (parent.GetNodeOrNull<Control>("HBoxContainer") is { } header) header.OffsetLeft = PanelLeft;

        var rail = new VBoxContainer { Name = "ModifierTabs", MouseFilter = Control.MouseFilterEnum.Ignore };
        rail.AddThemeConstantOverride("separation", 6);
        parent.AddChild(rail);
        rail.AnchorLeft = rail.AnchorRight = rail.AnchorTop = rail.AnchorBottom = 0;
        rail.OffsetLeft = PanelLeft + PanelPadding;
        rail.OffsetRight = rail.OffsetLeft + TabWidth;
        rail.OffsetTop = top + TopGap + 6;
        rail.OffsetBottom = top + TopGap + 6 + 10 * (TabHeight + 6);
        // The header holds the loadout select; its open list must draw and take clicks above the panel and tabs.
        if (parent.GetNodeOrNull<Control>("HBoxContainer") is { } headerRow) parent.MoveChild(headerRow, -1);
        void Fit() => FitScrollbar(list, panel);
        list.Resized += Fit;
        Callable.From(Fit).CallDeferred();
        return rail;
    }

    // The scrollbar track runs the full height of the panel and its right edge closes the panel.
    private static void FitScrollbar(NCustomRunModifiersList list, Control panel)
    {
        if (!GodotObject.IsInstanceValid(panel) || list.GetNodeOrNull<Control>("Scrollbar") is not { } bar) return;
        var box = panel.GetGlobalRect();
        var track = bar.GetGlobalRect();
        if (track.Size.Y <= 0) return;
        bar.OffsetTop += box.Position.Y - track.Position.Y;
        bar.OffsetBottom += box.End.Y - track.End.Y;
        panel.OffsetRight += track.End.X - box.End.X;
    }

    // Text tabs; the open one sits on the card library's teal header banner. Hover matches the settings tabs.
    private static (NButton Tab, TabVisual Visual) CreateTab(VBoxContainer rail, string title)
    {
        var tab = new NButton
        {
            Name = "Tab", CustomMinimumSize = new Vector2(TabWidth, TabHeight), FocusMode = Control.FocusModeEnum.All,
            MouseFilter = Control.MouseFilterEnum.Stop, PivotOffset = new Vector2(TabWidth / 2, TabHeight / 2)
        };
        rail.AddChild(tab);
        var (banner, material) = VanillaUi.Banner(0.9f);
        tab.AddChild(banner);
        banner.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", 14);
        margin.AddThemeConstantOverride("margin_right", 8);
        tab.AddChild(margin);
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 6);
        margin.AddChild(row);
        var label = VanillaUi.Label(title, 24, VanillaUi.HalfCream);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        // No clipping: the outline would be cut at the label edge.
        VanillaUi.Outline(label);
        row.AddChild(label);
        var (badge, badgeLabel) = VanillaUi.Badge(30);
        badge.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        badge.Visible = false;
        row.AddChild(badge);

        var visual = new TabVisual { Banner = banner, Material = material, Label = label, Badge = badge, BadgeLabel = badgeLabel };
        var mouseInside = false;
        void Enter()
        {
            if (visual.Hovered) return;
            visual.Hovered = true;
            VanillaUi.HoverIn(tab, material, ref visual.Tween);
            PaintTab(visual);
        }
        void Exit()
        {
            if (!visual.Hovered || tab.HasFocus() || mouseInside) return;
            visual.Hovered = false;
            VanillaUi.HoverOut(tab, material, ref visual.Tween, 1.2f, 0.9f);
            PaintTab(visual);
        }
        tab.MouseEntered += () => { mouseInside = true; Enter(); };
        tab.FocusEntered += Enter;
        tab.MouseExited += () => { mouseInside = false; Exit(); };
        tab.FocusExited += () => Callable.From(Exit).CallDeferred();
        PaintTab(visual);
        return (tab, visual);
    }

    private static void PaintTab(TabVisual visual)
    {
        if (!GodotObject.IsInstanceValid(visual.Label)) return;
        visual.Banner.Modulate = new Color(1, 1, 1, visual.Selected ? 1 : visual.Hovered ? 0.45f : 0);
        visual.Label.AddThemeColorOverride("font_color", visual.Disabled ? new Color(VanillaUi.Cream, 0.2f) :
            visual.Hovered ? VanillaUi.Gold : visual.Selected ? VanillaUi.Cream : VanillaUi.HalfCream);
    }

    private const string AdvancedTitleKey = "ultimate_custom_run.advanced.title";
    private const string AdvancedDescriptionKey = "ultimate_custom_run.advanced.description";

    // Advanced mode is a setting for the whole list, so it sits under the tab rail instead of heading every tab.
    // The full explanation is in a hover tip; the row itself only names the setting.
    private static NRunModifierTickbox CreateAdvancedToggle(NCustomRunModifiersList list, VBoxContainer rail)
    {
        var russian = LocManager.Instance.CultureInfo.TwoLetterISOLanguageName == "ru";
        var title = russian ? "Расширенный режим" : "Advanced mode";
        var description = russian ? "Параметры забега и ползунки значений модификаторов." : "Run Parameters and modifier value sliders.";
        LocManager.Instance.GetTable("modifiers").MergeWith(new Dictionary<string, string>
        {
            [AdvancedTitleKey] = title, [AdvancedDescriptionKey] = description
        });

        var parent = rail.GetParent<Control>();
        var slot = new VBoxContainer { Name = "AdvancedModeSlot", MouseFilter = Control.MouseFilterEnum.Ignore };
        slot.AddThemeConstantOverride("separation", 6);
        parent.AddChild(slot);
        // Anchored to the bottom like the panel, so it stays at the panel's lower edge.
        slot.AnchorLeft = slot.AnchorRight = 0;
        slot.AnchorTop = slot.AnchorBottom = 1;
        slot.OffsetLeft = rail.OffsetLeft;
        slot.OffsetRight = rail.OffsetRight;
        slot.OffsetBottom = PanelBottom(parent) - 8;
        slot.OffsetTop = slot.OffsetBottom - 90;
        slot.Alignment = BoxContainer.AlignmentMode.End;
        slot.AddChild(new ColorRect
        {
            Color = new Color(VanillaUi.Cream, 0.12f), CustomMinimumSize = new Vector2(0, 2), MouseFilter = Control.MouseFilterEnum.Ignore
        });

        var toggle = ModifierRandomizerUi.AddOption(list, slot, "AdvancedModeToggle", title, "",
            () => AdvancedMode.Enabled, value => AdvancedMode.SetEnabled(list, value));
        toggle.Ready += () =>
        {
            var label = toggle.GetNode<MegaCrit.Sts2.addons.mega_text.MegaRichTextLabel>("HBoxContainer/Description");
            label.AutoSizeEnabled = false;
            foreach (var size in new[] { "normal_font_size", "bold_font_size" }) label.AddThemeFontSizeOverride(size, 22);
            label.Text = $"[gold]{title}[/gold]";
        };
        toggle.MouseEntered += () => NHoverTipSet.CreateAndShow(toggle,
            new HoverTip(new LocString("modifiers", AdvancedTitleKey), new LocString("modifiers", AdvancedDescriptionKey), null!),
            HoverTipAlignment.Right);
        toggle.MouseExited += () => NHoverTipSet.Remove(toggle);
        return toggle;
    }

    // The modifier panel's bottom offset (it is anchored to the bottom of its parent).
    private static float PanelBottom(Control parent) => parent.GetNodeOrNull<Control>("ModifierPanel")?.OffsetBottom ?? 0;

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
            // Empty groups keep their place in the rail but cannot be opened.
            section.Tab.Visible = true;
            section.Tab.FocusMode = section.Available ? Control.FocusModeEnum.All : Control.FocusModeEnum.None;
            section.Tab.MouseFilter = section.Available ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
            section.Visual.Disabled = !section.Available;
            if (section.Visual.Disabled && section.Visual.Hovered)
            {
                section.Visual.Hovered = false;
                section.Visual.Tween?.Kill();
                section.Tab.Scale = Vector2.One;
            }
            section.Visual.Selected = selected;
            PaintTab(section.Visual);
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
            if (!GodotObject.IsInstanceValid(section.Visual.Badge)) continue;
            var count = section.Group is ModifierGroup.Randomizer or ModifierGroup.Disabled ? 0 :
                section.ChildList.GetChildren().OfType<NRunModifierTickbox>().Count(row =>
                    row.IsTicked && row.Modifier != null && !ModifierVariantUi.IsHiddenVariant(row.Modifier));
            section.Visual.Badge.Visible = count > 0;
            section.Visual.BadgeLabel.Text = count.ToString();
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
        if (!GodotObject.IsInstanceValid(content)) return;
        // The list was narrowed after the scene set the content width; follow the visible mask instead, so
        // expanding rows (sliders, wrapped descriptions) end at the scrollbar rather than under it.
        var width = content.GetParent() is Control mask ? mask.Size.X - Math.Max(0, content.Position.X) : content.Size.X;
        content.Size = new Vector2(width, content.GetCombinedMinimumSize().Y);
        ReportOverflow(content);
    }

    private static string? lastOverflow;

    // Logs the chain of controls that make the list wider than its mask, once per distinct cause.
    private static void ReportOverflow(VBoxContainer content)
    {
        if (content.GetParent() is not Control mask) return;
        var limit = mask.Size.X * 0.979f;
        if (content.GetCombinedMinimumSize().X <= limit + 1) return;
        var chain = new List<string>();
        Control node = content;
        while (true)
        {
            chain.Add($"{node.Name}({node.GetType().Name}) {node.GetCombinedMinimumSize().X:0}");
            var widest = node.GetChildren().OfType<Control>().Where(child => child.Visible)
                .OrderByDescending(child => child.GetCombinedMinimumSize().X).FirstOrDefault();
            if (widest == null || widest.GetCombinedMinimumSize().X < 1) break;
            node = widest;
        }
        var text = string.Join(" > ", chain);
        if (text == lastOverflow) return;
        lastOverflow = text;
        GD.Print($"[Ultimate Custom Run] Modifier list is wider than its panel ({limit:0}): {text}");
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
        return SelectedSection(layout) is { } section ? FocusableControls(section).ToList() : [];
    }

    // Up and down move within the open tab; right jumps to the tab column and left returns to the list.
    private static void UpdateFocus(Layout layout)
    {
        var controls = ContentControls(layout);
        var tabs = layout.Sections.Where(section => section.Available).Select(section => (Control)section.Tab).ToList();
        // The Advanced mode switch closes the rail's focus column.
        if (layout.AdvancedToggle is { Visible: true } toggle && toggle.FocusMode == Control.FocusModeEnum.All) tabs.Add(toggle);
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
            if (child.Name == DeckPickerUi.NodeName)
                foreach (var card in child.FindChildren("*", "Control", true, false).OfType<NButton>())
                    if (card.FocusMode == Control.FocusModeEnum.All) yield return card;
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
