using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.sts2.Core.Nodes.TopBar;

namespace UltimateCustomRun;

// The top bar lays modifier icons in one row after the room icons, so a long modifier list runs under
// the timer, map and deck on the right. Icons that do not fit are hidden and replaced by one "+N" icon
// whose hover shows the tips of every hidden modifier. Runs after the ascension grouping.
[HarmonyPatch(typeof(NTopBar), nameof(NTopBar.Initialize))]
internal static class TopBarModifierOverflow
{
    private sealed class State
    {
        internal required NTopBar TopBar;
        internal required Control Modifiers;
        internal required Container Left;
        internal required Container Right;
        internal required NTopBarModifier Overflow;
        internal required TextureRect Icon;
        internal required Label Count;
        internal List<IHoverTip> Tips = new();
        internal int Shown = -1;
        internal int Total = -1;
    }

    private const string OverflowName = "UcrModifierOverflow";
    // Matches the separation of the left-aligned top bar row.
    private const float Gap = 24;
    private const float IconWidth = 56;

    private static readonly FieldInfo HoverTipField = AccessTools.Field(typeof(NTopBarModifier), "_hoverTip");
    private static readonly ConditionalWeakTable<NTopBar, State> States = new();
    private static readonly ConditionalWeakTable<NTopBarModifier, State> Overflows = new();

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Low)]
    private static void Postfix(NTopBar __instance, IRunState runState)
    {
        if (runState.Modifiers.Count == 0) return;
        var state = States.GetValue(__instance, topBar => Create(topBar, runState));
        // Keep the overflow icon after the modifier icons added by this Initialize call.
        state.Modifiers.MoveChild(state.Overflow, -1);
        state.Shown = state.Total = -1;
        Callable.From(() => Fit(state)).CallDeferred();
    }

    private static State Create(NTopBar topBar, IRunState runState)
    {
        var modifiers = topBar.GetNode<Control>("%Modifiers");
        // A real modifier icon, so hover and controller focus behave exactly like its neighbours;
        // OnFocusPatch swaps its single tip for the hidden modifiers' tips.
        var overflow = NTopBarModifier.Create(runState.Modifiers[0])!;
        overflow.Name = OverflowName;
        overflow.Visible = false;
        modifiers.AddChild(overflow);
        var icon = overflow.GetNode<TextureRect>("Icon");
        icon.Modulate = new Color(1, 1, 1, 0.45f);
        var count = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorLeft = 0, AnchorRight = 1, AnchorTop = 0, AnchorBottom = 1, OffsetTop = -3, OffsetBottom = -3
        };
        // Styled like the floor number beside it.
        if (topBar.GetNodeOrNull<Label>("%FloorNumLabel") is { } floor)
        {
            count.AddThemeFontOverride("font", floor.GetThemeFont("font"));
            foreach (var color in new[] { "font_color", "font_shadow_color", "font_outline_color" })
                count.AddThemeColorOverride(color, floor.GetThemeColor(color));
            foreach (var constant in new[] { "shadow_offset_x", "shadow_offset_y", "outline_size" })
                count.AddThemeConstantOverride(constant, floor.GetThemeConstant(constant));
        }
        count.AddThemeFontSizeOverride("font_size", 28);
        overflow.AddChild(count);

        var state = new State
        {
            TopBar = topBar, Modifiers = modifiers, Left = topBar.LeftContainer, Right = topBar.RightContainer,
            Overflow = overflow, Icon = icon, Count = count
        };
        Overflows.AddOrUpdate(overflow, state);
        // Either row moving (gold digits, potion slots, window size) changes the free space. Fit only touches
        // the modifier row when the count changes, so the re-sort it causes ends here.
        state.Left.Connect(Container.SignalName.SortChildren, Callable.From(() => Fit(state)));
        state.Right.Connect(Container.SignalName.SortChildren, Callable.From(() => Fit(state)));
        return state;
    }

    [HarmonyPatch(typeof(NTopBarModifier), "OnFocus")]
    private static class OnFocusPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(NTopBarModifier __instance)
        {
            if (!Overflows.TryGetValue(__instance, out var state)) return true;
            ShowTips(state);
            return false;
        }
    }

    private static void ShowTips(State state)
    {
        if (state.Tips.Count == 0) return;
        var overflow = state.Overflow;
        NHoverTipSet.CreateAndShow(overflow, state.Tips)
            ?.SetAlignmentBelow(overflow.GlobalPosition + new Vector2(0, overflow.Size.Y * overflow.GetGlobalTransform().Scale.Y + 20));
    }

    private static void Fit(State state)
    {
        if (!GodotObject.IsInstanceValid(state.TopBar) || !state.Modifiers.IsVisibleInTree()) return;
        var icons = state.Modifiers.GetChildren().OfType<NTopBarModifier>().Where(icon => icon != state.Overflow && !icon.IsQueuedForDeletion()).ToList();
        // The first right-aligned element that is always laid out (timer, or the map when the timer is off).
        // The save indicator and its padding before it only flash briefly.
        var obstacle = state.Right.GetChildren().OfType<Control>()
            .SkipWhile(child => child.Name != "Padding").Skip(1).FirstOrDefault(child => child.Visible);
        if (obstacle == null) return;
        var scale = state.Modifiers.GetGlobalTransform().Scale.X;
        if (scale <= 0) return;
        var available = (obstacle.GlobalPosition.X - state.Modifiers.GlobalPosition.X) / scale - Gap;
        var capacity = Math.Max(1, (int)Math.Floor(available / IconWidth));
        var shown = icons.Count <= capacity ? icons.Count : capacity - 1;
        if (shown == state.Shown && icons.Count == state.Total) return;
        state.Shown = shown;
        state.Total = icons.Count;

        for (var i = 0; i < icons.Count; i++) icons[i].Visible = i < shown;
        var hidden = icons.Skip(shown).ToList();
        state.Overflow.Visible = hidden.Count > 0;
        state.Tips = hidden.Select(TipOf).OfType<IHoverTip>().ToList();
        state.Icon.Texture = hidden.FirstOrDefault()?.GetNodeOrNull<TextureRect>("Icon")?.Texture;
        state.Count.Text = "+" + hidden.Count;
        NHoverTipSet.Remove(state.Overflow);
        // Hidden icons must leave controller navigation; the game skips invisible elements.
        Callable.From(state.TopBar.UpdateNavigation).CallDeferred();
    }

    // The modifier's own tip (or the grouped ascension tip), with its icon so the list stays recognizable.
    private static IHoverTip? TipOf(NTopBarModifier icon)
    {
        var tip = HoverTipField.GetValue(icon);
        if (tip is not HoverTip) return null;
        var texture = icon.GetNodeOrNull<TextureRect>("Icon")?.Texture;
        if (texture != null) typeof(HoverTip).GetProperty(nameof(HoverTip.Icon))!.SetValue(tip, texture);
        return (HoverTip)tip;
    }
}
