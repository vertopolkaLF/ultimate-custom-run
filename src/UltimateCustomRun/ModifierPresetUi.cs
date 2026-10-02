using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Models;

namespace UltimateCustomRun;

internal static class ModifierPresetUi
{
    private const string DropdownScene = "res://scenes/screens/char_select/char_select_act_dropdown.tscn";
    private const string DropdownItemScene = "res://scenes/ui/dropdown_item.tscn";
    private const string PopupScene = "res://scenes/ui/generic_popup.tscn";

    private sealed class DropdownState
    {
        internal required NCustomRunModifiersList ModifierList;
        internal List<ModifierPreset> Presets = [];
    }

    private sealed class ScreenState
    {
        internal required NActDropdown Dropdown;
        internal required NButton SaveButton;
        internal required NCustomRunModifiersList ModifierList;
    }

    private static readonly ConditionalWeakTable<NActDropdown, DropdownState> Dropdowns = new();
    private static readonly ConditionalWeakTable<NCustomRunScreen, ScreenState> Screens = new();
    private static readonly Color ButtonColor = Color.FromHtml("36566b");
    private static readonly Color ButtonHoverColor = Color.FromHtml("496f83");
    private static readonly Color ButtonBorderColor = Color.FromHtml("a6bdc8");

    internal static void AddControls(NCustomRunScreen screen)
    {
        if (Screens.TryGetValue(screen, out _)) return;

        var randomizeButton = screen.GetNode<NCustomRunRandomizeButton>("%CustomRunRandomizeButton");
        var row = randomizeButton.GetParent<HBoxContainer>();
        var rowIndex = randomizeButton.GetIndex();
        row.RemoveChild(randomizeButton);
        randomizeButton.QueueFree();
        row.AddThemeConstantOverride("separation", 20);

        var modifierList = (NCustomRunModifiersList)AccessTools.Field(typeof(NCustomRunScreen), "_modifiersList")!.GetValue(screen)!;
        var dropdown = ResourceLoader.Load<PackedScene>(DropdownScene).Instantiate<NActDropdown>();
        dropdown.Name = "ModifierPresetDropdown";
        dropdown.CustomMinimumSize = new Vector2(272, 56);
        dropdown.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        dropdown.GetNode<Control>("CurrentOption").CustomMinimumSize = new Vector2(272, 40);
        Dropdowns.Add(dropdown, new DropdownState { ModifierList = modifierList });
        row.AddChild(dropdown);
        row.MoveChild(dropdown, rowIndex);
        RebuildOptions(dropdown);

        var saveButton = CreateSaveButton();
        saveButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(button =>
        {
            _ = OpenSaveDialog(screen, dropdown);
        }));
        row.AddChild(saveButton);
        saveButton.FocusNeighborLeft = saveButton.GetPathTo(dropdown);
        saveButton.FocusPrevious = saveButton.GetPathTo(dropdown);
        dropdown.FocusNeighborRight = dropdown.GetPathTo(saveButton);
        dropdown.FocusNext = dropdown.GetPathTo(saveButton);

        Screens.Add(screen, new ScreenState { Dropdown = dropdown, SaveButton = saveButton, ModifierList = modifierList });
        UpdateAvailability(screen);
    }

    internal static void UpdateAvailability(NCustomRunScreen screen)
    {
        if (!Screens.TryGetValue(screen, out var state)) return;
        var mode = (MultiplayerUiMode)AccessTools.Field(typeof(NCustomRunScreen), "_uiMode")!.GetValue(screen)!;
        var editable = mode is MultiplayerUiMode.Singleplayer or MultiplayerUiMode.Host;
        state.Dropdown.Visible = editable;
        state.SaveButton.Visible = editable;
        state.Dropdown.FocusMode = editable ? Control.FocusModeEnum.All : Control.FocusModeEnum.None;
        state.SaveButton.FocusMode = editable ? Control.FocusModeEnum.All : Control.FocusModeEnum.None;
    }

    private static NButton CreateSaveButton()
    {
        var button = new NButton
        {
            Name = "SaveModifierPresetButton",
            CustomMinimumSize = new Vector2(136, 56),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            FocusMode = Control.FocusModeEnum.All,
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        var background = new Panel
        {
            Name = "Background",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            FocusMode = Control.FocusModeEnum.None
        };
        var style = new StyleBoxFlat
        {
            BgColor = ButtonColor,
            BorderColor = ButtonBorderColor,
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8
        };
        background.AddThemeStyleboxOverride("panel", style);
        button.AddChild(background);
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var label = new Label
        {
            Name = "Label",
            Text = IsRussian() ? "Сохранить" : "Save",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            FocusMode = Control.FocusModeEnum.None
        };
        label.AddThemeFontOverride("font", ResourceLoader.Load<Font>("res://themes/kreon_bold_shared.tres"));
        label.AddThemeFontSizeOverride("font_size", 25);
        label.AddThemeColorOverride("font_color", Color.FromHtml("fff6e2"));
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.5f));
        label.AddThemeConstantOverride("shadow_offset_x", 2);
        label.AddThemeConstantOverride("shadow_offset_y", 2);
        button.AddChild(label);
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        button.MouseEntered += () => style.BgColor = ButtonHoverColor;
        button.MouseExited += () => style.BgColor = ButtonColor;
        return button;
    }

    private static void RebuildOptions(NActDropdown dropdown, string? selectedName = null)
    {
        if (!Dropdowns.TryGetValue(dropdown, out var state) || !GodotObject.IsInstanceValid(dropdown)) return;
        state.Presets = ModifierPresetStore.Load();

        var items = dropdown.GetNode<VBoxContainer>("DropdownContainer/VBoxContainer");
        foreach (var oldItem in items.GetChildren())
        {
            items.RemoveChild(oldItem);
            oldItem.QueueFree();
        }

        if (state.Presets.Count == 0)
            AddDropdownItem(items, IsRussian() ? "Нет сохранённых наборов" : "No saved loadouts", null, dropdown, state);
        else
            foreach (var preset in state.Presets)
                AddDropdownItem(items, preset.Name, preset, dropdown, state);

        items.GetParent<NDropdownContainer>().RefreshLayout();
        var label = dropdown.GetNode<MegaLabel>("CurrentOption/Label");
        label.SetTextAutoSize(selectedName ?? (IsRussian() ? "Выбрать набор" : "Select loadout"));
    }

    private static void AddDropdownItem(
        VBoxContainer items, string text, ModifierPreset? preset, NActDropdown dropdown, DropdownState state)
    {
        var item = ResourceLoader.Load<PackedScene>(DropdownItemScene).Instantiate<NDropdownItem>();
        item.Selected += _ =>
        {
            AccessTools.Method(typeof(NDropdown), "CloseDropdown")!.Invoke(dropdown, null);
            if (preset == null) return;
            dropdown.GetNode<MegaLabel>("CurrentOption/Label").SetTextAutoSize(preset.Name);
            ApplyPreset(state.ModifierList, preset);
        };
        items.AddChild(item);
        item.Text = text;
    }

    private static void ApplyPreset(NCustomRunModifiersList list, ModifierPreset preset)
    {
        var tickboxes = (List<NRunModifierTickbox>)AccessTools.Field(typeof(NCustomRunModifiersList), "_modifierTickboxes")!.GetValue(list)!;
        var selected = new List<ModifierModel>();
        foreach (var entry in preset.Modifiers)
        {
            var source = tickboxes.Select(tickbox => tickbox.Modifier)
                .FirstOrDefault(modifier => modifier != null && string.Equals(modifier.Id.ToString(), entry.Id, StringComparison.Ordinal));
            if (source == null) continue;

            var modifier = (ModifierModel)source.MutableClone();
            if (entry.Value is { } value && ModifierValues.For(modifier) != null)
                ModifierValues.Set(modifier, value);
            selected.Add(modifier);
        }

        list.SetTickedModifiers(selected);
    }

    private static async Task OpenSaveDialog(NCustomRunScreen screen, NActDropdown dropdown)
    {
        if (!Screens.TryGetValue(screen, out var state) || !GodotObject.IsInstanceValid(state.ModifierList)) return;
        var modals = NModalContainer.Instance;
        if (modals == null || modals.OpenModal != null) return;

        var validationError = false;
        while (true)
        {
            var popup = ResourceLoader.Load<PackedScene>(PopupScene).Instantiate<NGenericPopup>();
            popup.Name = "ModifierPresetNamePopup";
            modals.Add(popup);

            var verticalPopup = popup.GetNode<NVerticalPopup>("VerticalPopup");
            var russian = IsRussian();
            var confirmation = popup.WaitForConfirmation(
                new LocString("main_menu_ui", "ABANDON_RUN_CONFIRMATION.body"),
                new LocString("main_menu_ui", "ABANDON_RUN_CONFIRMATION.header"),
                new LocString("main_menu_ui", "GENERIC_POPUP.cancel"),
                new LocString("main_menu_ui", "GENERIC_POPUP.confirm"));
            verticalPopup.SetText(russian ? "Сохранить набор" : "Save loadout", string.Empty);
            verticalPopup.YesButton.SetText(russian ? "Сохранить" : "Save");
            verticalPopup.NoButton.SetText(russian ? "Отмена" : "Cancel");

            var body = verticalPopup.GetNode<RichTextLabel>("Description");
            body.Visible = false;
            var input = new LineEdit
            {
                Name = "LoadoutNameInput",
                PlaceholderText = validationError
                    ? (russian ? "Введите название набора" : "Enter a loadout name")
                    : (russian ? "Название набора" : "Loadout name"),
                CustomMinimumSize = new Vector2(400, 64),
                Size = new Vector2(400, 64),
                FocusMode = Control.FocusModeEnum.All,
                MouseFilter = Control.MouseFilterEnum.Stop,
                MaxLength = 64,
                Alignment = HorizontalAlignment.Center,
                ExpandToTextLength = false,
                ZIndex = 1
            };
            input.AddThemeFontOverride("font", ResourceLoader.Load<Font>("res://themes/kreon_regular_shared.tres"));
            input.AddThemeFontSizeOverride("font_size", 26);
            input.AddThemeColorOverride("font_color", Color.FromHtml("fff6e2"));
            input.AddThemeColorOverride("font_placeholder_color", new Color(1, 0.9647f, 0.8863f, 0.6f));
            verticalPopup.AddChild(input);
            input.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
            input.OffsetLeft = -200;
            input.OffsetTop = -32;
            input.OffsetRight = 200;
            input.OffsetBottom = 32;
            Callable.From(input.GrabFocus).CallDeferred();

            var confirmed = await confirmation;
            var name = input.Text.Trim();
            modals.Clear();

            if (!confirmed) return;
            if (name.Length == 0)
            {
                validationError = true;
                continue;
            }

            try
            {
                ModifierPresetStore.Save(name, state.ModifierList.GetModifiersTickedOn());
                RebuildOptions(dropdown, name);
            }
            catch (Exception ex)
            {
                GD.PushError($"[Ultimate Custom Run] Could not save modifier preset: {ex}");
            }
            return;
        }
    }

    private static bool IsRussian() => LocManager.Instance.CultureInfo.TwoLetterISOLanguageName == "ru";
}

[HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen._Ready))]
internal static class ModifierPresetScreenPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCustomRunScreen __instance) => ModifierPresetUi.AddControls(__instance);
}

[HarmonyPatch(typeof(NCustomRunScreen), "AfterInitialized")]
internal static class ModifierPresetAvailabilityPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCustomRunScreen __instance) => ModifierPresetUi.UpdateAvailability(__instance);
}

[HarmonyPatch(typeof(NCustomRunScreen), "OnRandomizePressed")]
internal static class ModifierPresetDisableRandomizePatch
{
    [HarmonyPrefix]
    private static bool Prefix() => false;
}
