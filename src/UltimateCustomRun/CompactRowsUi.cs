using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.addons.mega_text;

namespace UltimateCustomRun;

// Modifier rows show the name on its own line and the full description below it in a dimmer, smaller
// font that keeps the game's highlight colors.
// The original description label stays (hidden) because other code still writes the full sentence into it.
internal static class CompactRowsUi
{
    private sealed class Parts
    {
        internal required MegaRichTextLabel Title;
        internal required MegaRichTextLabel Summary;
        internal required bool Negative;
    }

    private static readonly ConditionalWeakTable<NRunModifierTickbox, Parts> Rows = new();
    private const int SummarySize = 20;

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
        summary.FitContent = true;
        summary.ScrollActive = false;
        summary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        summary.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        summary.CustomMinimumSize = Vector2.Zero;
        summary.MouseFilter = Control.MouseFilterEnum.Ignore;
        summary.AddThemeFontOverride("normal_font", ResourceLoader.Load<Font>(VanillaUi.RegularFont));
        foreach (var size in new[] { "normal_font_size", "bold_font_size", "italics_font_size", "bold_italics_font_size", "mono_font_size" })
            summary.AddThemeFontSizeOverride(size, SummarySize);
        // Plain words are dimmed; highlighted numbers and keywords keep their own colors.
        summary.AddThemeColorOverride("default_color", new Color(VanillaUi.Cream, 0.62f));
        column.AddChild(summary);
        line.AddChild(column);
        line.MoveChild(column, description.GetIndex() + 1);
        description.Visible = false;

        Rows.Add(row, new Parts { Title = title, Summary = summary, Negative = negative });
        Update(row);
    }

    internal static void Update(NRunModifierTickbox row)
    {
        if (!Rows.TryGetValue(row, out var parts) || row.Modifier is not { } modifier) return;
        if (!GodotObject.IsInstanceValid(parts.Title)) return;
        var title = modifier.Title.GetFormattedText();
        if (ModifierVariantUi.IsParent(modifier)) title += " [color=#ff9c3d]*[/color]";
        parts.Title.Text = $"[color={(parts.Negative ? "red" : "green")}]{title}[/color]";
        parts.Summary.Text = modifier.Description.GetFormattedText().Trim();
    }
}
