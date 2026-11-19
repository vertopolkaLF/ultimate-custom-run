using Godot;

namespace UltimateCustomRun;

// Small helpers that build controls from the game's own fonts, textures and shader, so added UI matches vanilla screens.
internal static class VanillaUi
{
    internal static readonly Color Cream = new(1, 0.9647059f, 0.8862745f);
    internal static readonly Color HalfCream = new(1, 0.9647059f, 0.8862745f, 0.5f);
    internal static readonly Color Gold = new(0.937255f, 0.784314f, 0.317647f);
    internal static readonly Color Teal = new(0.333333f, 0.580392f, 0.647059f);
    internal static readonly Color Green = Color.FromHtml("7fff00");
    internal static readonly Color Red = Color.FromHtml("ff6b64");
    internal static readonly Color Blue = new(0.529f, 0.808f, 0.922f);
    internal const string BoldFont = "res://themes/kreon_bold_shared.tres";
    internal const string RegularFont = "res://themes/kreon_regular_shared.tres";
    // Teal banner behind the card library's sort and filter headers.
    internal const string BannerTexture = "res://images/ui/reward_screen/reward_item_button.png";
    // Dark rounded box behind the Ascension panel.
    internal const string BoxTexture = "res://images/ui/tiny_nine_patch.png";
    internal const string BadgeTexture = "res://images/packed/card_library/cost_tickbox.png";
    private static readonly StringName ShaderV = new("v");

    internal static Label Label(string text, int size, Color color, bool bold = true)
    {
        var label = new Label { Text = text, VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontOverride("font", ResourceLoader.Load<Font>(bold ? BoldFont : RegularFont));
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    internal static void Shadow(Label label)
    {
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.5f));
        label.AddThemeConstantOverride("shadow_offset_x", 3);
        label.AddThemeConstantOverride("shadow_offset_y", 2);
    }

    internal static void Outline(Label label, int size = 10)
    {
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.5f));
        label.AddThemeConstantOverride("outline_size", size);
    }

    internal static NinePatchRect Box(Color modulate)
    {
        var box = new NinePatchRect
        {
            Texture = ResourceLoader.Load<Texture2D>(BoxTexture),
            PatchMarginLeft = 12, PatchMarginTop = 12, PatchMarginRight = 12, PatchMarginBottom = 12,
            Modulate = modulate, MouseFilter = Control.MouseFilterEnum.Ignore
        };
        return box;
    }

    // The banner texture only has a finished end on the left, so the right half mirrors the left half.
    internal static (Control Image, ShaderMaterial Material) Banner(float value = 0.8f)
    {
        var material = new ShaderMaterial { Shader = ResourceLoader.Load<Shader>("res://shaders/hsv.gdshader") };
        material.SetShaderParameter("h", 1.0f);
        material.SetShaderParameter("s", 0.8f);
        material.SetShaderParameter(ShaderV, value);
        var texture = ResourceLoader.Load<Texture2D>(BannerTexture);
        var half = new AtlasTexture { Atlas = texture, Region = new Rect2(0, 0, texture.GetWidth() / 2f, texture.GetHeight()) };
        var root = new Control { Name = "Banner", MouseFilter = Control.MouseFilterEnum.Ignore };
        foreach (var right in new[] { false, true })
        {
            var image = new TextureRect
            {
                Texture = half, FlipH = right, Material = material, MouseFilter = Control.MouseFilterEnum.Ignore,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
                AnchorLeft = right ? 0.5f : 0, AnchorRight = right ? 1 : 0.5f, AnchorTop = 0, AnchorBottom = 1
            };
            root.AddChild(image);
        }
        return (root, material);
    }

    // Modifier titles may carry bbcode, which hover tip titles show as raw text.
    internal static MegaCrit.Sts2.Core.HoverTips.HoverTip Tip(MegaCrit.Sts2.Core.Localization.LocString title,
        MegaCrit.Sts2.Core.Localization.LocString description)
    {
        object tip = new MegaCrit.Sts2.Core.HoverTips.HoverTip(title, description, null!);
        var plain = System.Text.RegularExpressions.Regex.Replace(title.GetFormattedText(), @"\[[^\]]*\]", "").Trim();
        typeof(MegaCrit.Sts2.Core.HoverTips.HoverTip).GetProperty("Title")!.SetValue(tip, plain);
        return (MegaCrit.Sts2.Core.HoverTips.HoverTip)tip;
    }

    private static Texture2D? radioOn, radioOff;
    private static readonly Dictionary<string, Texture2D?> Assets = [];

    // A PNG bundled in the assembly's Assets folder, or null when it is missing.
    internal static Texture2D? Asset(string name)
    {
        if (Assets.TryGetValue(name, out var cached)) return cached;
        Texture2D? texture = null;
        using (var stream = typeof(VanillaUi).Assembly.GetManifestResourceStream("UltimateCustomRun.Assets." + name))
        {
            if (stream != null)
            {
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                var image = new Image();
                if (image.LoadPngFromBuffer(buffer.ToArray()) == Error.Ok) texture = ImageTexture.CreateFromImage(image);
            }
        }
        return Assets[name] = texture;
    }

    // The mod's radio art (drawn to match the game's checkbox), bundled in the assembly.
    internal static Texture2D Radio(bool on)
    {
        if ((on ? radioOn : radioOff) is { } cached) return cached;
        var texture = Asset(on ? "radio_ticked.png" : "radio_unticked.png");
        // Without the bundled art, fall back to the game's own checkbox.
        texture ??= ResourceLoader.Load<Texture2D>(on
            ? "res://images/packed/common_ui/checkbox_ticked.png" : "res://images/packed/common_ui/checkbox_unticked.png");
        if (on) radioOn = texture; else radioOff = texture;
        return texture;
    }

    // Turns a game tickbox (modifier row, variant choice) into a radio button: one of several exclusive options.
    internal static void UseRadio(Control tickbox)
    {
        var visuals = tickbox.GetNodeOrNull<Control>("HBoxContainer/TickboxVisuals") ?? tickbox.GetNodeOrNull<Control>("%TickboxVisuals");
        if (visuals?.GetNodeOrNull<TextureRect>("Ticked") is { } ticked) ticked.Texture = Radio(true);
        if (visuals?.GetNodeOrNull<TextureRect>("NotTicked") is { } unticked) unticked.Texture = Radio(false);
    }

    internal const string HexTexture = "res://images/ui/reward_screen/reward_skip_button.png";

    // The reward screen's hexagonal button. The texture is drawn at its own aspect (scaled to the height) and
    // nine-patched horizontally, so the pointed ends keep their angle at any width. h/s/v tint it through the
    // game's hsv shader: (1, 1, v) is the native teal, (0.48, 1.5, 0.8) the green Proceed/Feedback button.
    internal static (Control Root, ShaderMaterial Material) Hex(float h, float s, float v)
    {
        var material = new ShaderMaterial { Shader = ResourceLoader.Load<Shader>("res://shaders/hsv.gdshader") };
        material.SetShaderParameter("h", h);
        material.SetShaderParameter("s", s);
        material.SetShaderParameter(ShaderV, v);
        var root = new Control { Name = "Hex", MouseFilter = Control.MouseFilterEnum.Ignore };
        var texture = ResourceLoader.Load<Texture2D>(HexTexture);
        var image = new NinePatchRect
        {
            Texture = texture, Material = material, MouseFilter = Control.MouseFilterEnum.Ignore,
            PatchMarginLeft = 64, PatchMarginRight = 64, PatchMarginTop = 24, PatchMarginBottom = 24
        };
        root.AddChild(image);
        void Fit()
        {
            if (root.Size.Y <= 0) return;
            var scale = root.Size.Y / texture.GetHeight();
            image.Scale = Vector2.One * scale;
            image.Size = new Vector2(root.Size.X / scale, texture.GetHeight());
        }
        root.Resized += Fit;
        Fit();
        return (root, material);
    }

    internal static void SetBrightness(ShaderMaterial material, float value) => material.SetShaderParameter(ShaderV, value);

    // Count badge drawn on the card library's cost tickbox.
    internal static (Control Root, Label Label) Badge(float size = 32)
    {
        var root = new Control { Name = "Badge", MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(size, size) };
        var image = new TextureRect
        {
            Texture = ResourceLoader.Load<Texture2D>(BadgeTexture),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SelfModulate = Teal, MouseFilter = Control.MouseFilterEnum.Ignore
        };
        root.AddChild(image);
        image.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var label = Label("", 18, Cream);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        Outline(label, 8);
        root.AddChild(label);
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return (root, label);
    }

    // The settings tabs' hover: brighten and grow at once, then ease back.
    internal static void HoverIn(Control control, ShaderMaterial? material, ref Tween? tween, float brightness = 1.2f)
    {
        // NTickbox: a 0.05 s tween up to x1.05 and the hover brightness.
        tween?.Kill();
        if (!GodotObject.IsInstanceValid(control) || !control.IsInsideTree()) return;
        tween = control.CreateTween().SetParallel();
        tween.TweenProperty(control, "scale", Vector2.One * 1.05f, 0.05);
        if (material != null)
            tween.TweenMethod(Callable.From<float>(value => SetBrightness(material, value)),
                (float)material.GetShaderParameter(ShaderV), brightness, 0.05);
    }

    internal static void HoverOut(Control control, ShaderMaterial? material, ref Tween? tween, float from = 1.2f, float to = 0.8f)
    {
        tween?.Kill();
        if (!GodotObject.IsInstanceValid(control) || !control.IsInsideTree()) return;
        tween = control.CreateTween().SetParallel();
        tween.TweenProperty(control, "scale", Vector2.One, 0.5).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Expo);
        if (material != null)
            tween.TweenMethod(Callable.From<float>(value => SetBrightness(material, value)),
                    (float)material.GetShaderParameter(ShaderV), to, 0.5)
                .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Expo);
    }
}
