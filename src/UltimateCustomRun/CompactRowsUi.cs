using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Godot;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.addons.mega_text;

namespace UltimateCustomRun;

// Modifier rows show the name and a one-line summary that keeps the game's highlight colors. When the
// summary has to be cut, the full description is shown in a native hover tip instead.
// The original description label stays (hidden) because other code still writes the full sentence into it.
internal static partial class CompactRowsUi
{
    private sealed class Parts
    {
        internal required MegaRichTextLabel Title;
        internal required MegaRichTextLabel Summary;
        internal required bool Negative;
        internal string Full = "";
        internal bool Truncated;
    }

    private static readonly ConditionalWeakTable<NRunModifierTickbox, Parts> Rows = new();
    private const int SummarySize = 20;
    private const string Ellipsis = "…";

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
        // Copies keep the game's fonts, named bbcode colors and text effects.
        var title = (MegaRichTextLabel)description.Duplicate();
        title.Name = "Title";
        title.CustomMinimumSize = Vector2.Zero;
        title.AutowrapMode = TextServer.AutowrapMode.Off;
        title.MouseFilter = Control.MouseFilterEnum.Ignore;
        column.AddChild(title);

        var summary = (MegaRichTextLabel)description.Duplicate();
        summary.Name = "Summary";
        summary.AutoSizeEnabled = false;
        summary.FitContent = false;
        summary.ScrollActive = false;
        summary.AutowrapMode = TextServer.AutowrapMode.Off;
        summary.ClipContents = true;
        summary.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        summary.CustomMinimumSize = new Vector2(0, 28);
        summary.MouseFilter = Control.MouseFilterEnum.Ignore;
        var font = ResourceLoader.Load<Font>(VanillaUi.RegularFont);
        summary.AddThemeFontOverride("normal_font", font);
        foreach (var size in new[] { "normal_font_size", "bold_font_size", "italics_font_size", "bold_italics_font_size", "mono_font_size" })
            summary.AddThemeFontSizeOverride(size, SummarySize);
        // Plain words are dimmed; highlighted numbers and keywords keep their own colors.
        summary.AddThemeColorOverride("default_color", new Color(VanillaUi.Cream, 0.62f));
        column.AddChild(summary);
        line.AddChild(column);
        line.MoveChild(column, description.GetIndex() + 1);
        description.Visible = false;

        Rows.Add(row, new Parts { Title = title, Summary = summary, Negative = negative });
        summary.Resized += () => Fit(row);
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
        parts.Full = modifier.Description.GetFormattedText().Replace('\n', ' ').Trim();
        Fit(row);
    }

    // Cuts the rich text to the visible width, closing any open tags, and remembers whether it was cut.
    private static void Fit(NRunModifierTickbox row)
    {
        if (!Rows.TryGetValue(row, out var parts) || !GodotObject.IsInstanceValid(parts.Summary)) return;
        var font = parts.Summary.GetThemeFont("normal_font");
        var plain = Tags().Replace(parts.Full, "");
        var width = parts.Summary.Size.X - 6;
        // Keywords may render in other fonts, so scale the estimate by the label's real line width.
        parts.Summary.Text = parts.Full;
        var estimate = font.GetStringSize(plain, HorizontalAlignment.Left, -1, SummarySize).X;
        var actual = parts.Summary.GetContentWidth();
        var scale = estimate > 0 && actual > 0 ? actual / estimate : 1;
        float Measure(string text) => font.GetStringSize(text, HorizontalAlignment.Left, -1, SummarySize).X * scale;
        if (width <= 0 || Measure(plain) <= width)
        {
            parts.Truncated = false;
            return;
        }
        var room = width - Measure(Ellipsis);
        int low = 0, high = plain.Length;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (Measure(plain[..middle]) <= room) low = middle;
            else high = middle - 1;
        }
        parts.Truncated = true;
        parts.Summary.Text = Cut(parts.Full, plain[..low].TrimEnd().Length);
    }

    private static string Cut(string text, int visible)
    {
        var result = new StringBuilder();
        var open = new Stack<string>();
        var count = 0;
        for (var i = 0; i < text.Length && count < visible; i++)
        {
            if (text[i] == '[' && text.IndexOf(']', i) is var end and > 0)
            {
                var tag = text.Substring(i + 1, end - i - 1);
                if (tag.StartsWith('/')) { if (open.Count > 0) open.Pop(); }
                else open.Push(tag.Split(' ', '=')[0]);
                result.Append(text, i, end - i + 1);
                i = end;
                continue;
            }
            result.Append(text[i]);
            count++;
        }
        result.Append(Ellipsis);
        while (open.Count > 0) result.Append("[/").Append(open.Pop()).Append(']');
        return result.ToString();
    }

    private static void ShowTip(NRunModifierTickbox row)
    {
        if (row.Modifier is not { } modifier || !row.IsVisibleInTree()) return;
        NHoverTipSet.Remove(row);
        if (!Rows.TryGetValue(row, out var parts) || !parts.Truncated) return;
        NHoverTipSet.CreateAndShow(row, VanillaUi.Tip(modifier.Title, modifier.Description), HoverTipAlignment.Left);
    }
}
