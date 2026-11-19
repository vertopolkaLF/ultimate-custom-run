using Godot;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.addons.mega_text;

namespace UltimateCustomRun;

// The Custom Mode rules note ("no Epochs, Achievements or new Ascensions") moves from a line at the bottom of
// the screen into a warning icon beside the title, shown as a native hover tip.
internal static class DisclaimerUi
{
    private const string IconPath = "res://images/ui/language_warning.png";
    private const float IconSize = 34;

    internal static void Attach(NCustomRunScreen screen)
    {
        var title = screen.GetNodeOrNull<MegaLabel>("%CustomModeTitle");
        if (screen.GetNodeOrNull<Control>("%Disclaimer") is { } disclaimer)
        {
            disclaimer.Visible = false;
            // The screen may show it again on its own; keep it hidden.
            disclaimer.VisibilityChanged += () => { if (disclaimer.Visible) disclaimer.Visible = false; };
        }
        if (title == null || title.GetNodeOrNull("RulesWarning") != null) return;

        var icon = new TextureRect
        {
            Name = "RulesWarning", Texture = ResourceLoader.Load<Texture2D>(IconPath),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Size = new Vector2(IconSize, IconSize), PivotOffset = new Vector2(IconSize / 2, IconSize / 2),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        title.AddChild(icon);
        void Place()
        {
            if (!GodotObject.IsInstanceValid(icon)) return;
            var font = title.GetThemeFont("font");
            var size = title.GetThemeFontSize("font_size");
            var width = font.GetStringSize(title.Text, HorizontalAlignment.Left, -1, size).X;
            var start = title.HorizontalAlignment switch
            {
                HorizontalAlignment.Center => (title.Size.X - width) / 2,
                HorizontalAlignment.Right => title.Size.X - width,
                _ => 0
            };
            icon.Position = new Vector2(start + width + 14, (title.Size.Y - IconSize) / 2);
        }
        title.Resized += Place;
        Callable.From(Place).CallDeferred();

        Tween? tween = null;
        icon.MouseEntered += () =>
        {
            VanillaUi.HoverIn(icon, null, ref tween);
            var text = new LocString("main_menu_ui", "CUSTOM_RUN_SCREEN.disclaimer").GetFormattedText().TrimStart('*', ' ');
            NHoverTipSet.CreateAndShow(icon,
                new HoverTip(new LocString("main_menu_ui", "CUSTOM_RUN_SCREEN.CUSTOM_MODE_TITLE"), text, null),
                HoverTipAlignment.Right);
        };
        icon.MouseExited += () =>
        {
            VanillaUi.HoverOut(icon, null, ref tween);
            NHoverTipSet.Remove(icon);
        };
    }
}
