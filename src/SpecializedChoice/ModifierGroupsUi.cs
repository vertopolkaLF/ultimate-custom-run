using System.Runtime.CompilerServices;
using OpCodes = System.Reflection.Emit.OpCodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Models;

namespace SpecializedChoice;

[HarmonyPatch(typeof(NCustomRunModifiersList), nameof(NCustomRunModifiersList._Ready))]
internal static class ModifierGroupsUi
{
    private sealed class Section
    {
        internal required NButton Header;
        internal required MarginContainer Body;
        internal required TextureRect Arrow;
        internal required IReadOnlyList<NRunModifierTickbox> Rows;
        internal bool Expanded = true;
    }
    private sealed class Layout
    {
        internal List<Section> Sections = [];
    }
    private static readonly ConditionalWeakTable<NCustomRunModifiersList, Layout> Layouts = new();
    private static readonly Color NegativeColor = Color.FromHtml("ff6b64");

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

    internal static Control? DefaultFocus(NCustomRunModifiersList list) =>
        Layouts.TryGetValue(list, out var layout) ? layout.Sections.FirstOrDefault()?.Header : null;

    private static void Build(NCustomRunModifiersList list)
    {
        if (!GodotObject.IsInstanceValid(list) || Layouts.TryGetValue(list, out _)) return;
        var content = list.GetNode<VBoxContainer>("ScrollContainer/Mask/Content");
        var rows = (List<NRunModifierTickbox>)AccessTools.Field(typeof(NCustomRunModifiersList), "_modifierTickboxes").GetValue(list)!;
        var negatives = ModelDb.BadModifiers.Select(m => m.GetType()).ToHashSet();
        var font = ResourceLoader.Load<Font>("res://themes/kreon_bold_shared.tres");
        var arrowTexture = ResourceLoader.Load<Texture2D>("res://images/packed/common_ui/settings_tiny_right_arrow.png");
        var layout = new Layout();
        Layouts.Add(list, layout);
        content.AddThemeConstantOverride("separation", 12);

        foreach (var (group, title) in ModifierGroups.Sections)
        {
            var children = rows.Where(row => row.Modifier != null && ModifierGroups.Classify(row.Modifier, negatives) == group).ToList();
            var sectionRoot = new VBoxContainer { Name = group.ToString(), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            sectionRoot.AddThemeConstantOverride("separation", 4);
            var header = new NButton
            {
                Name = "SectionHeader", CustomMinimumSize = new Vector2(0, 56),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.All,
                MouseFilter = Control.MouseFilterEnum.Stop, TooltipText = title
            };
            var highlight = new ColorRect
            {
                Name = "Highlight", Color = new Color(1, 0.6392157f, 0.44705883f, 0.15f),
                MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false
            };
            header.AddChild(highlight);
            highlight.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            margin.AddThemeConstantOverride("margin_left", 8);
            margin.AddThemeConstantOverride("margin_right", 8);
            header.AddChild(margin);
            margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            var heading = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            heading.AddThemeConstantOverride("separation", 8);
            margin.AddChild(heading);
            var arrowSlot = new Control { CustomMinimumSize = new Vector2(40, 48), MouseFilter = Control.MouseFilterEnum.Ignore };
            var arrow = new TextureRect
            {
                Texture = arrowTexture, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore, Rotation = Mathf.Pi / 2,
                PivotOffset = new Vector2(16, 16)
            };
            arrowSlot.AddChild(arrow);
            arrow.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
            arrow.OffsetLeft = -16; arrow.OffsetTop = -16; arrow.OffsetRight = 16; arrow.OffsetBottom = 16;
            heading.AddChild(arrowSlot);
            var label = new Label
            {
                Text = title, VerticalAlignment = VerticalAlignment.Center,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore
            };
            label.AddThemeFontOverride("font", font);
            label.AddThemeFontSizeOverride("font_size", 28);
            label.AddThemeColorOverride("font_color", group == ModifierGroup.Negatives ? NegativeColor : new Color(1, 0.9647059f, 0.8862745f));
            label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.5f));
            label.AddThemeConstantOverride("shadow_offset_x", 3);
            label.AddThemeConstantOverride("shadow_offset_y", 2);
            heading.AddChild(label);
            var line = new ColorRect
            {
                Color = new Color(1, 0.9647059f, 0.8862745f, 0.15f),
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            header.AddChild(line);
            line.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide);
            line.OffsetTop = -1;
            var body = new MarginContainer { Name = "Children", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            // Native checkbox center is 28 px; header arrow center is 8 + 40/2 = 28 px.
            body.AddThemeConstantOverride("margin_left", 0);
            var childList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            childList.AddThemeConstantOverride("separation", 8);
            body.AddChild(childList);
            sectionRoot.AddChild(header);
            sectionRoot.AddChild(body);
            content.AddChild(sectionRoot);
            foreach (var row in children)
            {
                row.Reparent(childList);
                row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                // Custom-only positive modifiers are absent from the global daily pool.
                if (row.Modifier is { } modifier && ModifierListPatch.IsCustomOnly(modifier))
                {
                    var text = new MegaCrit.Sts2.Core.Localization.LocString("main_menu_ui", "CUSTOM_RUN_SCREEN.MODIFIER_LABEL");
                    text.Add("color", "green");
                    text.Add("modifier_title", modifier.Title.GetFormattedText());
                    text.Add("modifier_description", modifier.Description.GetFormattedText());
                    row.GetNode<MegaCrit.Sts2.addons.mega_text.MegaRichTextLabel>("HBoxContainer/Description").Text = text.GetFormattedText();
                }
            }
            LinkedModifierChains.Attach(body, children);
            var section = new Section { Header = header, Body = body, Arrow = arrow, Rows = children };
            layout.Sections.Add(section);
            header.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => Toggle(content, layout, section)));
            header.MouseEntered += () => highlight.Visible = true;
            header.MouseExited += () => highlight.Visible = header.HasFocus();
            header.FocusEntered += () => highlight.Visible = true;
            header.FocusExited += () => highlight.Visible = false;
        }

        content.MinimumSizeChanged += () => Callable.From(() => ResizeContent(content)).CallDeferred();
        UpdateFocus(layout);
        ResizeContent(content);
        Callable.From(() => ResizeContent(content)).CallDeferred();
    }

    private static void Toggle(VBoxContainer content, Layout layout, Section section)
    {
        var focus = content.GetViewport().GuiGetFocusOwner();
        if (section.Expanded && focus != null && section.Body.IsAncestorOf(focus)) section.Header.GrabFocus();
        section.Expanded = !section.Expanded;
        section.Body.Visible = section.Expanded;
        section.Arrow.Rotation = section.Expanded ? Mathf.Pi / 2 : 0;
        UpdateFocus(layout);
        Callable.From(() => ResizeContent(content)).CallDeferred();
    }

    private static void ResizeContent(VBoxContainer content)
    {
        if (GodotObject.IsInstanceValid(content))
            content.Size = new Vector2(content.Size.X, content.GetCombinedMinimumSize().Y);
    }

    private static void UpdateFocus(Layout layout)
    {
        var controls = layout.Sections.SelectMany(section =>
            new Control[] { section.Header }.Concat(section.Expanded ? section.Rows : [])).ToList();
        for (var i = 0; i < controls.Count; i++)
        {
            var previous = controls[(i + controls.Count - 1) % controls.Count];
            var next = controls[(i + 1) % controls.Count];
            controls[i].FocusNeighborTop = controls[i].GetPathTo(previous);
            controls[i].FocusPrevious = controls[i].GetPathTo(previous);
            controls[i].FocusNeighborBottom = controls[i].GetPathTo(next);
            controls[i].FocusNext = controls[i].GetPathTo(next);
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
