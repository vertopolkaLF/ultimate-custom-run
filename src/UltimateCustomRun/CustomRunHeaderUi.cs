using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.addons.mega_text;

namespace UltimateCustomRun;

// The Custom Mode header: the title, the game mode select and a seed button share one row at the top of the
// left column, matching the Modifiers title, loadout select and Save button on the right. The seed is entered in
// a popup opened by that button; the game's own seed field stays hidden and keeps holding the value. Both columns then run from under the header almost to the screen bottom.
internal static class CustomRunHeaderUi
{
    // The screen's 1920x1080 design space. The left column spans these x values; the modifier panel starts right of it.
    internal const float ColumnLeft = 170, ColumnRight = 866;
    internal const float HeaderTop = 40, HeaderHeight = 60;
    // First y below the header for column content.
    internal const float ContentTop = 118;
    // Column content ends this far above the bottom of the screen.
    private const float BottomMargin = 60;
    private const float ControlHeight = 56, ModeWidth = 240, SeedWidth = 84, HeaderGap = 16;
    // Vanilla offsets from the bottom of the left column, moved down into the space the seed row used to take.
    private const float AscensionTop = -366, AscensionBottom = -249;
    private const float CharactersTop = -50, CharactersBottom = -10;
    private const float WarningIconRoom = 56;
    private const string PopupScene = "res://scenes/ui/generic_popup.tscn";

    private sealed class State
    {
        internal required NButton SeedButton;
        internal required Control Badge;
        internal required LineEdit Input;
    }

    private static readonly ConditionalWeakTable<NCustomRunScreen, State> States = new();

    internal static void Attach(NCustomRunScreen screen)
    {
        if (States.TryGetValue(screen, out _)) return;
        var left = screen.GetNode<Control>("LeftContainer");
        var header = left.GetNode<HBoxContainer>("HBoxContainer");
        var positioner = screen.GetNode<Control>("%GameModeDropdownContainer");
        var input = screen.GetNode<LineEdit>("%SeedInput");

        // One row for the game mode select and the seed button, ending at the column's right edge.
        header.MouseFilter = Control.MouseFilterEnum.Ignore;
        header.Alignment = BoxContainer.AlignmentMode.End;
        header.AddThemeConstantOverride("separation", (int)HeaderGap);
        header.OffsetLeft = ColumnLeft;
        header.OffsetRight = ColumnRight;
        header.OffsetTop = HeaderTop + (HeaderHeight - ControlHeight) / 2;
        header.OffsetBottom = header.OffsetTop + ControlHeight;
        positioner.CustomMinimumSize = new Vector2(ModeWidth, ControlHeight);
        if (input.GetParent() is Control seedRow) seedRow.Visible = false;
        StyleGameModeDropdown(screen.GetNode<NCustomGameModeDropdown>("%GameModeDropdown"));

        var (button, badge) = CreateSeedButton();
        header.AddChild(button);
        var state = new State { SeedButton = button, Badge = badge, Input = input };
        States.Add(screen, state);
        button.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(pressed => { _ = OpenSeedDialog(state); }));
        button.MouseEntered += () => ShowSeedTip(state);
        button.MouseExited += () => NHoverTipSet.Remove(button);
        button.TreeExiting += () => NHoverTipSet.Remove(button);
        input.TextChanged += _ => Refresh(state);
        Refresh(state);

        PlaceTitle(screen, header);
        PlaceColumns(screen, left);
    }

    private static void PlaceTitle(NCustomRunScreen screen, HBoxContainer header)
    {
        if (screen.GetNodeOrNull<MegaLabel>("%CustomModeTitle") is not { } title) return;
        title.HorizontalAlignment = HorizontalAlignment.Left;
        title.VerticalAlignment = VerticalAlignment.Center;
        title.AnchorLeft = title.AnchorRight = title.AnchorTop = title.AnchorBottom = 0;
        title.OffsetLeft = ColumnLeft;
        title.OffsetTop = HeaderTop;
        title.OffsetBottom = HeaderTop + HeaderHeight;
        // The title shrinks its font to fit, so it stops short of the header controls and the rules warning icon.
        void Fit()
        {
            if (!GodotObject.IsInstanceValid(title) || !GodotObject.IsInstanceValid(header)) return;
            title.OffsetRight = ColumnRight - header.GetCombinedMinimumSize().X - WarningIconRoom;
        }
        header.MinimumSizeChanged += Fit;
        Fit();
        Callable.From(Fit).CallDeferred();
    }

    private static void PlaceColumns(NCustomRunScreen screen, Control left)
    {
        // Lobby players start right under the header, lined up with the Active modifiers panel below them.
        if (left.GetNodeOrNull<Control>("RemotePlayerContainer") is { } players)
        {
            var height = players.OffsetBottom - players.OffsetTop;
            players.OffsetLeft = ColumnLeft;
            players.OffsetRight = ColumnRight;
            players.OffsetTop = ContentTop;
            players.OffsetBottom = ContentTop + height;
        }
        if (left.GetNodeOrNull<Control>("AscensionPanel") is { } ascension)
        {
            ascension.OffsetTop = AscensionTop;
            ascension.OffsetBottom = AscensionBottom;
        }
        if (left.GetNodeOrNull<Control>("CharSelectButtons") is { } characters)
        {
            characters.OffsetTop = CharactersTop;
            characters.OffsetBottom = CharactersBottom;
        }

        // The modifier list grows into the old top and bottom margins; ModifierGroupsUi builds its panel from these
        // offsets once the list is ready, which happens after this screen's _Ready.
        var right = screen.GetNode<Control>("RightContainer");
        if (screen.GetNodeOrNull<Control>("%ModifiersList") is { } list)
        {
            list.OffsetTop = ContentTop - 6;
            list.OffsetBottom = -BottomMargin - 10;
        }
        if (right.GetNodeOrNull<HBoxContainer>("HBoxContainer") is { } rightHeader)
        {
            rightHeader.MouseFilter = Control.MouseFilterEnum.Ignore;
            rightHeader.OffsetTop = HeaderTop + (HeaderHeight - ControlHeight) / 2;
            rightHeader.OffsetBottom = rightHeader.OffsetTop + ControlHeight;
            // Push the loadout select and Save button to the panel's right edge.
            if (rightHeader.GetNodeOrNull<Control>("ModifiersTitle") is { } modifiersTitle)
            {
                var spacer = new Control
                {
                    Name = "HeaderSpacer", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore
                };
                rightHeader.AddChild(spacer);
                rightHeader.MoveChild(spacer, modifiersTitle.GetIndex() + 1);
            }
        }
    }

    // The game's mode dropdown keeps its logic, but looks like the mod's loadout select: a teal hexagon with a
    // gold label and arrow.
    private static void StyleGameModeDropdown(NCustomGameModeDropdown dropdown)
    {
        dropdown.CustomMinimumSize = new Vector2(ModeWidth, ControlHeight);
        dropdown.Size = new Vector2(ModeWidth, ControlHeight);
        var current = dropdown.GetNode<Control>("Container/CurrentOption");
        current.CustomMinimumSize = Vector2.Zero;
        current.Resized += () => current.PivotOffset = current.Size / 2;
        // Vanilla tints this rectangle on focus; keep it invisible behind the hexagon.
        current.GetNode<Control>("Highlight").SelfModulate = Colors.Transparent;
        var (hex, material) = VanillaUi.Hex(1, 1, ModifierPresetUi.TealV);
        current.AddChild(hex);
        current.MoveChild(hex, 0);
        hex.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var label = current.GetNode<MegaLabel>("Label");
        label.Modulate = Colors.White;
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        label.OffsetLeft = 30;
        label.OffsetRight = -62;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.ClipText = true;
        label.AddThemeColorOverride("font_color", VanillaUi.Gold);
        label.AddThemeColorOverride("font_outline_color", new Color(0.2166f, 0.317363f, 0.38f));
        label.AddThemeConstantOverride("outline_size", 10);
        label.AddThemeFontSizeOverride("font_size", 26);

        if (dropdown.GetNodeOrNull<Control>("Container/Arrow") is { } arrow)
        {
            arrow.Modulate = VanillaUi.Gold;
            arrow.Reparent(current);
            arrow.SetAnchorsPreset(Control.LayoutPreset.CenterRight);
            arrow.OffsetLeft = -60;
            arrow.OffsetRight = -34;
            arrow.OffsetTop = -13;
            arrow.OffsetBottom = 13;
            arrow.PivotOffset = new Vector2(13, 13);
        }
        if (dropdown.GetNodeOrNull<Control>("Container/DropdownContainer") is { } options)
        {
            options.OffsetLeft = -ModeWidth / 2;
            options.OffsetRight = ModeWidth / 2;
            options.OffsetTop = ControlHeight + 4;
            if (options.GetNodeOrNull<ColorRect>("ColorRect") is { } background) background.Color = Color.FromHtml("0d1519");
        }
        ModifierPresetUi.AddHover(dropdown, current, material, ModifierPresetUi.TealV);
    }

    private static (NButton Button, Control Badge) CreateSeedButton()
    {
        var button = new NButton
        {
            Name = "SeedButton", CustomMinimumSize = new Vector2(SeedWidth, ControlHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            FocusMode = Control.FocusModeEnum.All, MouseFilter = Control.MouseFilterEnum.Stop
        };
        button.Resized += () => button.PivotOffset = button.Size / 2;
        var (hex, material) = VanillaUi.Hex(1, 1, ModifierPresetUi.TealV);
        button.AddChild(hex);
        hex.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var icon = new TextureRect
        {
            Name = "Icon", Texture = VanillaUi.Asset("seed_icon.png"), MouseFilter = Control.MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
        };
        button.AddChild(icon);
        icon.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        icon.OffsetLeft = icon.OffsetTop = -18;
        icon.OffsetRight = icon.OffsetBottom = 18;

        // A green dot while a fixed seed is set.
        var badge = new Panel { Name = "SeedSetBadge", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        badge.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Color.FromHtml("7fd04b"), BorderColor = Color.FromHtml("10181c"),
            BorderWidthLeft = 3, BorderWidthTop = 3, BorderWidthRight = 3, BorderWidthBottom = 3,
            CornerRadiusTopLeft = 9, CornerRadiusTopRight = 9, CornerRadiusBottomLeft = 9, CornerRadiusBottomRight = 9
        });
        button.AddChild(badge);
        badge.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        badge.OffsetLeft = -26;
        badge.OffsetRight = -8;
        badge.OffsetTop = 2;
        badge.OffsetBottom = 20;
        ModifierPresetUi.AddHover(button, button, material, ModifierPresetUi.TealV);
        return (button, badge);
    }

    // The same generic popup as the loadout Save dialog: a text field with Apply and Cancel. An empty field
    // means a random seed. Clients follow the host's seed and only see it in the button's hover tip.
    private static async Task OpenSeedDialog(State state)
    {
        NHoverTipSet.Remove(state.SeedButton);
        var modals = NModalContainer.Instance;
        if (modals == null || modals.OpenModal != null || !state.Input.Editable) return;
        var russian = LocManager.Instance.CultureInfo.TwoLetterISOLanguageName == "ru";

        var popup = ResourceLoader.Load<PackedScene>(PopupScene).Instantiate<NGenericPopup>();
        popup.Name = "SeedPopup";
        modals.Add(popup);
        var verticalPopup = popup.GetNode<NVerticalPopup>("VerticalPopup");
        var confirmation = popup.WaitForConfirmation(
            new LocString("main_menu_ui", "ABANDON_RUN_CONFIRMATION.body"),
            new LocString("main_menu_ui", "ABANDON_RUN_CONFIRMATION.header"),
            new LocString("main_menu_ui", "GENERIC_POPUP.cancel"),
            new LocString("main_menu_ui", "GENERIC_POPUP.confirm"));
        verticalPopup.SetText(SeedTitle(), string.Empty);
        verticalPopup.YesButton.SetText(russian ? "Применить" : "Apply");
        verticalPopup.NoButton.SetText(russian ? "Отмена" : "Cancel");
        verticalPopup.GetNode<RichTextLabel>("Description").Visible = false;

        var input = new LineEdit
        {
            Name = "SeedDialogInput", Text = state.Input.Text, MaxLength = state.Input.MaxLength,
            PlaceholderText = new LocString("main_menu_ui", "CUSTOM_RUN_SCREEN.SEED_RANDOM_PLACEHOLDER").GetFormattedText(),
            CustomMinimumSize = new Vector2(400, 64), Size = new Vector2(400, 64),
            FocusMode = Control.FocusModeEnum.All, MouseFilter = Control.MouseFilterEnum.Stop,
            Alignment = HorizontalAlignment.Center, ExpandToTextLength = false, ZIndex = 1
        };
        input.AddThemeFontOverride("font", ResourceLoader.Load<Font>(VanillaUi.RegularFont));
        input.AddThemeFontSizeOverride("font_size", 28);
        input.AddThemeColorOverride("font_color", VanillaUi.Cream);
        input.AddThemeColorOverride("font_placeholder_color", new Color(VanillaUi.Cream, 0.6f));
        verticalPopup.AddChild(input);
        input.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        input.OffsetLeft = -200;
        input.OffsetTop = -32;
        input.OffsetRight = 200;
        input.OffsetBottom = 32;
        // Enter applies, like pressing the Apply button.
        input.TextSubmitted += _ => verticalPopup.YesButton.EmitSignal(NClickableControl.SignalName.Released, verticalPopup.YesButton);
        Callable.From(() =>
        {
            if (!GodotObject.IsInstanceValid(input)) return;
            input.GrabFocus();
            input.CaretColumn = input.Text.Length;
        }).CallDeferred();

        var confirmed = await confirmation;
        var seed = GodotObject.IsInstanceValid(input) ? input.Text.Trim() : null;
        modals.Clear();
        if (!confirmed || seed == null || !GodotObject.IsInstanceValid(state.Input)) return;
        state.Input.Text = seed;
        // Setting Text does not emit TextChanged, which is what stores the seed in the lobby.
        state.Input.EmitSignal(LineEdit.SignalName.TextChanged, seed);
        Refresh(state);
        if (NControllerManager.Instance?.IsUsingDirectionalNavigation == true) state.SeedButton.TryGrabFocus();
    }

    internal static void Refresh(NCustomRunScreen screen)
    {
        if (States.TryGetValue(screen, out var state)) Refresh(state);
    }

    private static void Refresh(State state)
    {
        if (!GodotObject.IsInstanceValid(state.Badge) || !GodotObject.IsInstanceValid(state.Input)) return;
        state.Badge.Visible = !string.IsNullOrWhiteSpace(state.Input.Text);
    }

    private static void ShowSeedTip(State state)
    {
        NHoverTipSet.Remove(state.SeedButton);
        var value = string.IsNullOrWhiteSpace(state.Input.Text)
            ? new LocString("main_menu_ui", "CUSTOM_RUN_SCREEN.SEED_RANDOM_PLACEHOLDER").GetFormattedText()
            : state.Input.Text;
        NHoverTipSet.CreateAndShow(state.SeedButton, new HoverTip(new LocString("main_menu_ui", "CUSTOM_RUN_SCREEN.SEED_LABEL"), value),
            HoverTipAlignment.Right);
    }

    private static string SeedTitle() =>
        new LocString("main_menu_ui", "CUSTOM_RUN_SCREEN.SEED_LABEL").GetFormattedText().Trim().TrimEnd(':', '：').Trim();

    // Vanilla points focus at the seed field, which now lives in the popup; the seed button takes its place.
    internal static void FixFocus(NCustomRunScreen screen)
    {
        if (!States.TryGetValue(screen, out var state) || !GodotObject.IsInstanceValid(state.SeedButton)) return;
        var button = state.SeedButton;
        var dropdown = screen.GetNode<NCustomGameModeDropdown>("%GameModeDropdown");
        var container = (Control)AccessTools.Field(typeof(NCustomRunScreen), "_charButtonContainer").GetValue(screen)!;
        var characters = container.GetChildren().OfType<NCharacterSelectButton>().ToList();
        button.FocusNeighborLeft = dropdown.Visible ? button.GetPathTo(dropdown) : button.GetPath();
        button.FocusNeighborRight = button.GetPath();
        button.FocusNeighborTop = button.GetPath();
        if (characters.FirstOrDefault() is { } first) button.FocusNeighborBottom = button.GetPathTo(first);
        if (dropdown.Visible) dropdown.FocusNeighborRight = dropdown.GetPathTo(button);
        else foreach (var character in characters) character.FocusNeighborTop = character.GetPathTo(button);
    }
}

[HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen.SeedChanged))]
internal static class CustomRunSeedChangedPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCustomRunScreen __instance) => CustomRunHeaderUi.Refresh(__instance);
}

[HarmonyPatch(typeof(NCustomRunScreen), "AfterInitialized")]
internal static class CustomRunHeaderInitializedPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCustomRunScreen __instance)
    {
        CustomRunHeaderUi.Refresh(__instance);
        CustomRunHeaderUi.FixFocus(__instance);
    }
}

[HarmonyPatch(typeof(NCustomRunScreen), "UpdateGameModeDropdownOptionsAndVisibility")]
internal static class CustomRunHeaderFocusPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCustomRunScreen __instance) => CustomRunHeaderUi.FixFocus(__instance);
}
