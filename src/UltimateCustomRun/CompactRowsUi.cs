using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Godot;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.addons.mega_text;

namespace UltimateCustomRun;

// Modifier rows show the name and a one-line summary; the full description moves to a native hover tip.
// The original description label stays (hidden) because other code still writes the full sentence into it.
internal static partial class CompactRowsUi
{
    private sealed class Parts
    {
        internal required MegaRichTextLabel Title;
        internal required Label Summary;
        internal required bool Negative;
    }

    private static readonly ConditionalWeakTable<NRunModifierTickbox, Parts> Rows = new();

    [GeneratedRegex(@"\[[^\]]*\]")]
    private static partial Regex Tags();

    internal static void Attach(NRunModifierTickbox row, bool negative)
    {
        if (row.Modifier == null || Rows.TryGetValue(row, out _)) return;
        var line = row.GetNodeOrNull<HBoxContainer>("HBoxContainer");
        var description = line?.GetNodeOrNull<MegaRichTextLabel>("Description");
        if (line == null || description == null) return;

        var column = new VBoxContainer
        {
            Name = "Compact", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore
        };
        column.AddThemeConstantOverride("separation", -4);
        // A copy keeps the game's fonts and its named bbcode colors.
        var title = (MegaRichTextLabel)description.Duplicate();
        title.Name = "Title";
        title.CustomMinimumSize = Vector2.Zero;
        title.AutowrapMode = TextServer.AutowrapMode.Off;
        title.MouseFilter = Control.MouseFilterEnum.Ignore;
        column.AddChild(title);
        var summary = new Label
        {
            Name = "Summary", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, MouseFilter = Control.MouseFilterEnum.Ignore
        };
        summary.AddThemeFontOverride("font", ResourceLoader.Load<Font>(VanillaUi.RegularFont));
        summary.AddThemeFontSizeOverride("font_size", 20);
        summary.AddThemeColorOverride("font_color", new Color(VanillaUi.Cream, 0.62f));
        column.AddChild(summary);
        line.AddChild(column);
        line.MoveChild(column, description.GetIndex() + 1);
        description.Visible = false;

        Rows.Add(row, new Parts { Title = title, Summary = summary, Negative = negative });
        row.MouseEntered += () => ShowTip(row);
        row.MouseExited += () => NHoverTipSet.Remove(row);
        row.VisibilityChanged += () => { if (!row.IsVisibleInTree()) NHoverTipSet.Remove(row); };
        Update(row);
    }

    internal static void Update(NRunModifierTickbox row)
    {
        if (!Rows.TryGetValue(row, out var parts) || row.Modifier is not { } modifier) return;
        if (!GodotObject.IsInstanceValid(parts.Title)) return;
        var title = modifier.Title.GetFormattedText();
        if (ModifierVariantUi.IsParent(modifier)) title += " [color=#ff9c3d]*[/color]";
        parts.Title.Text = $"[color={(parts.Negative ? "red" : "green")}]{title}[/color]";
        parts.Summary.Text = Plain(modifier.Description.GetFormattedText());
    }

    private static string Plain(string text) => Tags().Replace(text, "").Replace('\n', ' ').Trim();

    private static void ShowTip(NRunModifierTickbox row)
    {
        if (row.Modifier is not { } modifier || !row.IsVisibleInTree()) return;
        NHoverTipSet.Remove(row);
        NHoverTipSet.CreateAndShow(row, new HoverTip(modifier.Title, modifier.Description, null!), HoverTipAlignment.Left);
    }
}
