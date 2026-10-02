using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.RelicCollection;

namespace UltimateCustomRun;

internal static class HeadstartRelicUi
{
    private sealed class State
    {
        internal required IReadOnlyList<RelicModel> Relics;
        internal required HeadstartSelection Selection;
        internal required int Count;
        internal Control? First;
    }
    private static readonly ConditionalWeakTable<NChooseARelicSelection, State> Screens = new();
    private const string ScenePath = "res://scenes/screens/choose_a_relic_selection_screen.tscn";

    internal static async Task<IEnumerable<RelicModel>> Select(IReadOnlyList<RelicModel> relics, int count)
    {
        var overlays = NOverlayStack.Instance ?? throw new InvalidOperationException("Headstart requires the run overlay stack.");
        var screen = ResourceLoader.Load<PackedScene>(ScenePath).Instantiate<NChooseARelicSelection>();
        screen.Name = "HeadstartRelicSelection";
        Screens.Add(screen, new State { Relics = relics, Count = count, Selection = new(count, relics.Count) });
        AccessTools.Field(typeof(NChooseARelicSelection), "_relics").SetValue(screen, relics);
        overlays.Push(screen);
        return await screen.RelicsSelected();
    }

    internal static Control? DefaultFocus(NChooseARelicSelection screen) =>
        Screens.TryGetValue(screen, out var state) ? state.First : null;

    internal static bool Initialize(NChooseARelicSelection screen)
    {
        if (!Screens.TryGetValue(screen, out var state)) return false;
        var russian = LocManager.Instance.CultureInfo.TwoLetterISOLanguageName == "ru";
        var banner = screen.GetNode<NCommonBanner>("Banner");
        AccessTools.Field(typeof(NChooseARelicSelection), "_banner").SetValue(screen, banner);
        // The native banner occupies the middle of the screen; a container header
        // leaves the full browsing grid visible without overlapping its contents.
        banner.Visible = false;
        screen.GetNode<Control>("RelicRow").Visible = false;
        var skip = screen.GetNode<NButton>("SkipButton");
        skip.Disable();
        skip.Visible = false;
        skip.ProcessMode = Node.ProcessModeEnum.Disabled;

        var panel = new PanelContainer { Name = "HeadstartPanel", MouseFilter = Control.MouseFilterEnum.Stop };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.055f, 0.065f, 0.075f, 0.95f),
            ContentMarginLeft = 20, ContentMarginRight = 20, ContentMarginTop = 16, ContentMarginBottom = 16
        });
        screen.AddChild(panel);
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        panel.AnchorLeft = 0.08f;
        panel.AnchorRight = 0.92f;
        panel.AnchorTop = 0.1f;
        panel.AnchorBottom = 0.9f;
        var body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 12);
        panel.AddChild(body);
        var header = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(0, 44)
        };
        header.AddThemeFontOverride("font", ResourceLoader.Load<Font>("res://themes/kreon_bold_shared.tres"));
        header.AddThemeFontSizeOverride("font_size", 30);
        header.AddThemeColorOverride("font_color", Color.FromHtml("fff6e2"));
        body.AddChild(header);

        var search = new LineEdit
        {
            PlaceholderText = russian ? "Поиск реликвий" : "Search relics",
            CustomMinimumSize = new Vector2(0, 48), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.All
        };
        search.AddThemeFontOverride("font", ResourceLoader.Load<Font>("res://themes/kreon_regular_shared.tres"));
        search.AddThemeFontSizeOverride("font_size", 24);
        body.AddChild(search);
        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true
        };
        body.AddChild(scroll);
        var grid = new GridContainer { Columns = 10, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 8);
        scroll.AddChild(grid);
        var entries = new List<(NRelicCollectionEntry Entry, ColorRect Mark, RelicModel Relic)>();

        var confirm = CreateConfirmButton(russian);
        body.AddChild(confirm);
        void RefreshSelection()
        {
            header.Text = russian
                ? $"Headstart — выбрано {state.Selection.Indexes.Count}/{state.Count}"
                : $"Headstart — selected {state.Selection.Indexes.Count}/{state.Count}";
            confirm.SetEnabled(state.Selection.CanConfirm);
            foreach (var index in Enumerable.Range(0, entries.Count))
                entries[index].Mark.Visible = state.Selection.Indexes.Contains(index);
        }
        for (var index = 0; index < state.Relics.Count; index++)
        {
            var relic = state.Relics[index];
            var entry = NRelicCollectionEntry.Create(relic, ModelVisibility.Visible);
            entry.CustomMinimumSize = new Vector2(96, 96);
            entry.FocusMode = Control.FocusModeEnum.All;
            grid.AddChild(entry);
            var mark = new ColorRect
            {
                Color = new Color(0.9f, 0.65f, 0.2f, 0.28f), Visible = false,
                MouseFilter = Control.MouseFilterEnum.Ignore, FocusMode = Control.FocusModeEnum.None
            };
            entry.AddChild(mark);
            entry.MoveChild(mark, 0);
            mark.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            var choiceIndex = index;
            entry.Connect(NClickableControl.SignalName.Released, Callable.From<NClickableControl>(_ =>
            {
                if (state.Selection.Toggle(choiceIndex)) RefreshSelection();
            }));
            entries.Add((entry, mark, relic));
        }
        void RefreshFocus()
        {
            grid.Columns = Math.Max(1, (int)((scroll.Size.X - 20) / 104));
            var visible = entries.Where(entry => entry.Entry.Visible).Select(entry => (Control)entry.Entry).ToArray();
            state.First = visible.FirstOrDefault() ?? search;
            for (var index = 0; index < visible.Length; index++)
            {
                var entry = visible[index];
                entry.FocusNeighborLeft = entry.GetPathTo(visible[Math.Max(0, index - 1)]);
                entry.FocusNeighborRight = entry.GetPathTo(visible[Math.Min(visible.Length - 1, index + 1)]);
                entry.FocusNeighborTop = entry.GetPathTo(index < grid.Columns ? search : visible[index - grid.Columns]);
                entry.FocusNeighborBottom = entry.GetPathTo(index + grid.Columns < visible.Length ? visible[index + grid.Columns] : confirm);
                entry.FocusPrevious = entry.GetPathTo(index == 0 ? search : visible[index - 1]);
                entry.FocusNext = entry.GetPathTo(index + 1 < visible.Length ? visible[index + 1] : confirm);
            }
            search.FocusNeighborBottom = search.GetPathTo(state.First);
            search.FocusNext = search.GetPathTo(state.First);
            confirm.FocusNeighborTop = confirm.GetPathTo(visible.LastOrDefault() ?? search);
            confirm.FocusPrevious = confirm.FocusNeighborTop;
            confirm.FocusNext = confirm.GetPathTo(search);
        }
        search.TextChanged += text =>
        {
            foreach (var entry in entries)
                entry.Entry.Visible = string.IsNullOrWhiteSpace(text) ||
                    entry.Relic.Title.GetFormattedText().Contains(text.Trim(), StringComparison.CurrentCultureIgnoreCase);
            scroll.ScrollVertical = 0;
            RefreshFocus();
        };
        scroll.Resized += RefreshFocus;
        confirm.Connect(NClickableControl.SignalName.Released, Callable.From<NClickableControl>(_ =>
        {
            if (!state.Selection.CanConfirm) return;
            Headstart.ValidateSelection(state.Selection.Indexes, state.Count, state.Relics.Count);
            AccessTools.Field(typeof(NChooseARelicSelection), "_screenComplete").SetValue(screen, true);
            AccessTools.Field(typeof(NChooseARelicSelection), "_relicSelected").SetValue(screen, true);
            var completion = (TaskCompletionSource<IEnumerable<RelicModel>>)AccessTools.Field(
                typeof(NChooseARelicSelection), "_completionSource").GetValue(screen)!;
            completion.TrySetResult(state.Selection.Indexes.Select(index => state.Relics[index]).ToArray());
        }));
        RefreshSelection();
        RefreshFocus();
        Callable.From(RefreshFocus).CallDeferred();
        return true;
    }

    private static NButton CreateConfirmButton(bool russian)
    {
        var button = new NButton
        {
            Name = "ConfirmHeadstartRelics", CustomMinimumSize = new Vector2(220, 56),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            FocusMode = Control.FocusModeEnum.All, MouseFilter = Control.MouseFilterEnum.Stop
        };
        var panel = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat
        {
            BgColor = Color.FromHtml("36566b"), BorderColor = Color.FromHtml("a6bdc8"),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8
        };
        panel.AddThemeStyleboxOverride("panel", style);
        button.AddChild(panel);
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var label = new Label
        {
            Text = russian ? "Подтвердить" : "Confirm", HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeFontOverride("font", ResourceLoader.Load<Font>("res://themes/kreon_bold_shared.tres"));
        label.AddThemeFontSizeOverride("font_size", 25);
        label.AddThemeColorOverride("font_color", Color.FromHtml("fff6e2"));
        button.AddChild(label);
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        button.Connect(NClickableControl.SignalName.Focused, Callable.From<NClickableControl>(_ => style.BgColor = Color.FromHtml("496f83")));
        button.Connect(NClickableControl.SignalName.Unfocused, Callable.From<NClickableControl>(_ => style.BgColor = Color.FromHtml("36566b")));
        return button;
    }
}

[HarmonyPatch(typeof(NChooseARelicSelection), nameof(NChooseARelicSelection._Ready))]
internal static class HeadstartRelicScreenPatch
{
    [HarmonyPrefix]
    private static bool Prefix(NChooseARelicSelection __instance) => !HeadstartRelicUi.Initialize(__instance);
}

[HarmonyPatch(typeof(NChooseARelicSelection), nameof(NChooseARelicSelection.DefaultFocusedControl), MethodType.Getter)]
internal static class HeadstartRelicFocusPatch
{
    [HarmonyPrefix]
    private static bool Prefix(NChooseARelicSelection __instance, ref Control __result)
    {
        if (HeadstartRelicUi.DefaultFocus(__instance) is not { } focus) return true;
        __result = focus;
        return false;
    }
}
