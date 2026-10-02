using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace UltimateCustomRun;

internal static class SubmodifierUi
{
    private sealed record Row(NRunModifierTickbox Parent, NRunModifierTickbox Child, MarginContainer Root);
    private static readonly ConditionalWeakTable<NCustomRunModifiersList, List<Row>> Rows = new();

    internal static void Attach(NCustomRunModifiersList list, List<NRunModifierTickbox> tickboxes)
    {
        var rows = Rows.GetOrCreateValue(list);
        foreach (var child in tickboxes.Where(row => row.Modifier is SubmodifierModel))
        {
            var model = (SubmodifierModel)child.Modifier!;
            var parent = tickboxes.FirstOrDefault(row => row.Modifier?.GetType() == model.ParentType);
            if (parent == null) { child.Visible = false; continue; }
            var container = (VBoxContainer)parent.GetParent();
            var root = new MarginContainer { Name = model.Id.Entry + "Submodifier", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            root.AddThemeConstantOverride("margin_left", 52);
            container.AddChild(root);
            child.Reparent(root, false);
            container.MoveChild(root, Math.Min(parent.GetIndex() + 2, container.GetChildCount() - 1));
            rows.Add(new Row(parent, child, root));
        }
        Refresh(list);
    }

    internal static void Refresh(NCustomRunModifiersList list)
    {
        if (!Rows.TryGetValue(list, out var rows)) return;
        var editable = (MultiplayerUiMode)AccessTools.Field(typeof(NCustomRunModifiersList), "_mode").GetValue(list)!
            is MultiplayerUiMode.Singleplayer or MultiplayerUiMode.Host;
        foreach (var row in rows)
        {
            if (!row.Parent.IsTicked && list.GetViewport()?.GuiGetFocusOwner() is { } focus && row.Root.IsAncestorOf(focus)) row.Parent.GrabFocus();
            row.Root.Visible = row.Parent.IsTicked;
            row.Child.MouseFilter = editable ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
            row.Child.FocusMode = editable && row.Parent.IsTicked ? Control.FocusModeEnum.All : Control.FocusModeEnum.None;
        }
    }
}
