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
    private const string DeleteIconPath = "res://images/packed/main_menu/delete_button.png";
    private const float HorizontalPadding = 16;
    private const float RemoveButtonWidth = 44;
    private const float RemoveButtonGap = 8;

    private sealed class DropdownState
    {
        internal required NCustomRunModifiersList ModifierList;
        internal List<ModifierPreset> Presets = [];
        internal string? SelectedName;
    }

    private sealed class ScreenState
    {
        internal required NActDropdown Dropdown;
        internal required NButton SaveButton;
        internal required NCustomRunModifiersList ModifierList;
    }

    private static readonly ConditionalWeakTable<NActDropdown, DropdownState> Dropdowns = new();
    private static readonly ConditionalWeakTable<NCustomRunScreen, ScreenState> Screens = new();

    internal static void AddControls(NCustomRunScreen screen)
    {
        if (Screens.TryGetValue(screen, out _)) return;
        DisclaimerUi.Attach(screen);

        var randomizeButton = screen.GetNode<NCustomRunRandomizeButton>("%CustomRunRandomizeButton");
        var row = randomizeButton.GetParent<HBoxContainer>();
        var rowIndex = randomizeButton.GetIndex();
        row.RemoveChild(randomizeButton);
        randomizeButton.QueueFree();
        row.AddThemeConstantOverride("separation", 20);

        var modifierList = (NCustomRunModifiersList)AccessTools.Field(typeof(NCustomRunScreen), "_modifiersList")!.GetValue(screen)!;
        var dropdownSlot = new Control
        {
            Name = "ModifierPresetSlot",
            CustomMinimumSize = new Vector2(CustomRunHeaderUi.SelectWidth, 56),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        row.AddChild(dropdownSlot);
        row.MoveChild(dropdownSlot, rowIndex);

        var dropdown = ResourceLoader.Load<PackedScene>(DropdownScene).Instantiate<NActDropdown>();
        dropdown.Name = "ModifierPresetDropdown";
        dropdown.CustomMinimumSize = Vector2.Zero;
        dropdown.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        dropdown.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        dropdown.GetNode<Control>("CurrentOption").CustomMinimumSize = new Vector2(CustomRunHeaderUi.SelectWidth, 40);
        Dropdowns.Add(dropdown, new DropdownState { ModifierList = modifierList });
        dropdownSlot.AddChild(dropdown);
        dropdown.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        ApplySettingsDropdownAppearance(dropdown);
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
        CustomRunHeaderUi.Attach(screen);
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

    private static NButton CreateSaveButton() =>
        CreateButton("SaveModifierPresetButton", IsRussian() ? "Сохранить" : "Save", 136);

    // Green hexagonal button, the same art and tint as the game's Proceed and Feedback buttons; teal is the Skip button.
    internal static NButton CreateButton(string name, string text, float width, bool teal = false)
    {
        var button = new NButton
        {
            Name = name,
            CustomMinimumSize = new Vector2(width, 56),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            FocusMode = Control.FocusModeEnum.All,
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        button.Resized += () => button.PivotOffset = button.Size / 2;
        // The hex art is teal; h shifts its hue by (h - 1) turns, so 0.76 lands on the Feedback button's green.
        var (background, material) = teal ? VanillaUi.Hex(1, 1, TealV) : VanillaUi.Hex(0.76f, 1.3f, GreenV);
        background.Name = "Background";
        button.AddChild(background);
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var label = new Label
        {
            Name = "Label",
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            FocusMode = Control.FocusModeEnum.None
        };
        label.AddThemeFontOverride("font", ResourceLoader.Load<Font>("res://themes/kreon_bold_shared.tres"));
        label.AddThemeFontSizeOverride("font_size", 25);
        label.AddThemeColorOverride("font_color", Color.FromHtml("fff6e2"));
        label.AddThemeColorOverride("font_outline_color", teal ? new Color(0.2166f, 0.317363f, 0.38f) : new Color(0.13f, 0.24f, 0.1f));
        label.AddThemeConstantOverride("outline_size", 10);
        button.AddChild(label);
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        label.OffsetBottom = -2;
        AddHover(button, button, material, teal ? TealV : GreenV);
        return button;
    }

    internal const float GreenV = 0.8f, TealV = 0.9f;

    // The settings-button hover used across the mod: grow and brighten at once, ease back over half a second.
    internal static void AddHover(Control target, Control scaled, ShaderMaterial material, float baseV)
    {
        Tween? tween = null;
        var hovered = false;
        var focused = false;
        void Update()
        {
            // Grow from the middle; the size may have settled after the node was built.
            scaled.PivotOffset = scaled.Size / 2;
            if (hovered || focused) VanillaUi.HoverIn(scaled, material, ref tween, baseV + 0.3f);
            else VanillaUi.HoverOut(scaled, material, ref tween, baseV + 0.3f, baseV);
        }
        target.MouseEntered += () => { hovered = true; Update(); };
        target.MouseExited += () => { hovered = false; Update(); };
        if (target is NClickableControl clickable)
        {
            clickable.Connect(NClickableControl.SignalName.Focused, Callable.From<NClickableControl>(_ =>
            {
                // Mouse clicks also focus the button; show focus only for keyboard or controller navigation.
                focused = NControllerManager.Instance?.IsUsingDirectionalNavigation == true;
                Update();
            }));
            clickable.Connect(NClickableControl.SignalName.Unfocused, Callable.From<NClickableControl>(_ => { focused = false; Update(); }));
        }
    }

    private static void ApplySettingsDropdownAppearance(NActDropdown dropdown)
    {
        var label = dropdown.GetNode<MegaLabel>("CurrentOption/Label");
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        label.OffsetLeft = 30; // Clear of the hexagon's left point; the right side leaves room for the arrow.
        label.OffsetRight = -66;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.ClipText = true;
        label.MouseFilter = Control.MouseFilterEnum.Ignore;

        label.AddThemeColorOverride("font_color", VanillaUi.Gold);
        label.AddThemeColorOverride("font_outline_color", new Color(0.2166f, 0.317363f, 0.38f));
        label.AddThemeConstantOverride("outline_size", 10);

        // The closed select is the reward screen's teal hexagon, like the game's Skip button.
        var current = dropdown.GetNode<Control>("CurrentOption");
        current.Resized += () => current.PivotOffset = current.Size / 2;
        var highlight = current.GetNode<Panel>("Highlight");
        highlight.SelfModulate = Colors.Transparent;
        var (hex, material) = VanillaUi.Hex(1, 1, TealV);
        current.AddChild(hex);
        current.MoveChild(hex, 0);
        hex.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        // The arrow joins the hexagon so both grow together on hover.
        if (dropdown.GetNodeOrNull<Control>("Arrow") is { } arrow)
        {
            arrow.Modulate = VanillaUi.Gold;
            arrow.Reparent(current);
            // Inside the hexagon, clear of its pointed end, centered on the row. Its pivot is the image center
            // so the game's rotation turns it in place.
            arrow.SetAnchorsPreset(Control.LayoutPreset.CenterRight);
            arrow.OffsetLeft = -64;
            arrow.OffsetRight = -38;
            arrow.OffsetTop = -13;
            arrow.OffsetBottom = 13;
            arrow.PivotOffset = new Vector2(13, 13);
        }
        AddHover(dropdown, current, material, TealV);

        // The open list uses the same dark nine-patch as the game's info panels.
        var optionsPanel = dropdown.GetNode<Panel>("DropdownContainer/Highlight");
        optionsPanel.Modulate = Colors.White;
        optionsPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Color.FromHtml("0d1519"), BorderColor = new Color(VanillaUi.Teal, 0.6f),
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
            ShadowColor = new Color(0, 0, 0, 0.5f), ShadowSize = 8
        });
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

        AddDropdownItem(items, ModifierPresetStore.EmptyName, ModifierPresetStore.Empty, dropdown, state, false);
        foreach (var preset in state.Presets)
            AddDropdownItem(items, ModifierPresetStore.IsReservedName(preset.Name)
                ? preset.Name + (IsRussian() ? " (сохранённый)" : " (saved)") : preset.Name, preset, dropdown, state);

        items.GetParent<NDropdownContainer>().RefreshLayout();
        Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(items)) items.GetParent<NDropdownContainer>().RefreshLayout();
        }).CallDeferred();
        var label = dropdown.GetNode<MegaLabel>("CurrentOption/Label");
        state.SelectedName = selectedName;
        label.SetTextAutoSize(selectedName ?? (IsRussian() ? "Выбрать набор" : "Select loadout"));
    }

    private static void AddDropdownItem(
        VBoxContainer items, string text, ModifierPreset preset, NActDropdown dropdown, DropdownState state, bool removable = true)
    {
        var item = ResourceLoader.Load<PackedScene>(DropdownItemScene).Instantiate<NDropdownItem>();
        item.Selected += _ =>
        {
            AccessTools.Method(typeof(NDropdown), "CloseDropdown")!.Invoke(dropdown, null);
            state.SelectedName = preset.Name;
            dropdown.GetNode<MegaLabel>("CurrentOption/Label").SetTextAutoSize(preset.Name);
            ApplyPreset(state.ModifierList, preset);
        };
        items.AddChild(item);
        item.Text = text;
        var label = item.GetNode<MegaLabel>("Label");
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        label.OffsetLeft = HorizontalPadding;
        label.OffsetRight = -HorizontalPadding;
        label.HorizontalAlignment = HorizontalAlignment.Left;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.ClipText = true;
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        label.AddThemeFontOverride("font", ResourceLoader.Load<Font>(VanillaUi.RegularFont));
        label.AddThemeFontSizeOverride("font_size", 22);
        StyleDropdownItem(item, label, preset.Name == state.SelectedName, !removable);
        if (removable) AddRemoveButton(item, preset, dropdown, state);
    }

    // The game toggles the item's flat Highlight on hover and focus; it now drives a teal library banner and
    // gold text instead. The current loadout stays gold, and the empty entry reads as a quiet reset.
    private static void StyleDropdownItem(NDropdownItem item, Label label, bool current, bool reset)
    {
        var highlight = item.GetNode<ColorRect>("Highlight");
        highlight.Color = Colors.Transparent;
        var (banner, material) = VanillaUi.Banner(0.9f);
        banner.Visible = false;
        item.AddChild(banner);
        item.MoveChild(banner, 0);
        banner.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        banner.OffsetLeft = 6;
        banner.OffsetRight = -6;
        banner.OffsetTop = 2;
        banner.OffsetBottom = -2;
        var idle = current ? VanillaUi.Gold : reset ? new Color(VanillaUi.Cream, 0.5f) : VanillaUi.Cream;
        // Moving onto the trash button unfocuses the row in the game's logic; the row stays lit meanwhile, and
        // the button itself never hides, so it can always be clicked.
        var onRemove = false;
        void Paint()
        {
            if (!GodotObject.IsInstanceValid(label)) return;
            var lit = highlight.Visible || onRemove;
            banner.Visible = lit;
            label.AddThemeColorOverride("font_color", lit ? VanillaUi.Gold : idle);
            if (item.GetNodeOrNull<CanvasItem>("RemovePresetButton") is { } remove) remove.Modulate = new Color(1, 1, 1, lit ? 1 : 0.35f);
        }
        highlight.VisibilityChanged += Paint;
        Callable.From(() =>
        {
            if (item.GetNodeOrNull<Control>("RemovePresetButton") is { } remove)
            {
                remove.MouseEntered += () => { onRemove = true; Paint(); };
                remove.MouseExited += () => { onRemove = false; Paint(); };
            }
            Paint();
        }).CallDeferred();
    }

    private static void AddRemoveButton(NDropdownItem item, ModifierPreset preset, NActDropdown dropdown, DropdownState state)
    {
        var label = item.GetNode<MegaLabel>("Label");
        label.OffsetRight = -(HorizontalPadding + RemoveButtonWidth + RemoveButtonGap);
        label.ClipText = true;
        label.MouseFilter = Control.MouseFilterEnum.Ignore;

        var button = new ModifierPresetRemoveButton
        {
            Name = "RemovePresetButton", CustomMinimumSize = new Vector2(RemoveButtonWidth, 44),
            FocusMode = Control.FocusModeEnum.All, MouseFilter = Control.MouseFilterEnum.Stop,
            TooltipText = IsRussian() ? $"Удалить набор «{preset.Name}»" : $"Delete loadout: {preset.Name}"
        };
        item.AddChild(button);
        button.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.RightWide);
        button.OffsetLeft = -(HorizontalPadding + RemoveButtonWidth);
        button.OffsetRight = -HorizontalPadding;
        var icon = new TextureRect
        {
            Texture = ResourceLoader.Load<Texture2D>(DeleteIconPath),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        button.AddChild(icon);
        icon.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        icon.OffsetLeft = icon.OffsetTop = 8;
        icon.OffsetRight = icon.OffsetBottom = -8;
        // The trash icon shows only on the hovered row: quiet cream, red while it is the target.
        icon.Modulate = new Color(VanillaUi.Cream, 0.7f);
        void Target(bool on) => icon.Modulate = on ? VanillaUi.Red : new Color(VanillaUi.Cream, 0.7f);
        button.MouseEntered += () => Target(true);
        button.MouseExited += () => Target(false);
        button.Connect(NClickableControl.SignalName.Focused, Callable.From<NClickableControl>(_ => Target(true)));
        button.Connect(NClickableControl.SignalName.Unfocused, Callable.From<NClickableControl>(_ => Target(false)));
        button.Connect(NClickableControl.SignalName.Released, Callable.From<NClickableControl>(_ =>
            Callable.From(() => DeletePreset(dropdown, state, preset.Name)).CallDeferred()));
    }

    private static void DeletePreset(NActDropdown dropdown, DropdownState state, string name)
    {
        if (!GodotObject.IsInstanceValid(dropdown)) return;
        try
        {
            ModifierPresetStore.Delete(name);
            // Rebuild after the release signal returns; removing a row never applies a preset.
            var selected = string.Equals(state.SelectedName, name, StringComparison.OrdinalIgnoreCase) ? null : state.SelectedName;
            AccessTools.Method(typeof(NDropdown), "CloseDropdown")!.Invoke(dropdown, null);
            RebuildOptions(dropdown, selected);
        }
        catch (Exception ex) { GD.PushError($"[Ultimate Custom Run] Could not delete modifier preset: {ex}"); }
    }

    internal static void RefreshDropdownFocus(NActDropdown dropdown)
    {
        if (!Dropdowns.TryGetValue(dropdown, out _)) return;
        var items = dropdown.GetNode<VBoxContainer>("DropdownContainer/VBoxContainer").GetChildren().OfType<NDropdownItem>().ToArray();
        for (var index = 0; index < items.Length; index++)
        {
            var item = items[index];
            var previous = items[Math.Max(0, index - 1)];
            var next = items[Math.Min(items.Length - 1, index + 1)];
            var remove = item.GetNodeOrNull<NButton>("RemovePresetButton");
            item.FocusPrevious = item.GetPathTo(previous.GetNodeOrNull<NButton>("RemovePresetButton") ?? (Control)previous);
            item.FocusNext = item.GetPathTo(remove ?? (Control)next);
            if (remove == null) continue;
            // Native OpenDropdown rewrites row focus neighbors on each opening.
            item.FocusNeighborRight = item.GetPathTo(remove);
            item.FocusNext = item.GetPathTo(remove);
            remove.FocusNeighborLeft = remove.GetPathTo(item);
            remove.FocusPrevious = remove.GetPathTo(item);
            remove.FocusNeighborRight = remove.GetPath();
            remove.FocusNeighborTop = remove.GetPathTo(previous.GetNodeOrNull<NButton>("RemovePresetButton") ?? (Control)previous);
            remove.FocusNeighborBottom = remove.GetPathTo(next.GetNodeOrNull<NButton>("RemovePresetButton") ?? (Control)next);
            remove.FocusNext = remove.FocusNeighborBottom;
        }
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
            if (modifier is CustomRunParameters parameters)
            {
                var values = entry.Parameters ?? CustomRunParameterValues.Default;
                foreach (var parameter in Enum.GetValues<CustomRunParameter>())
                    CustomRunParameterValuesStore.Set(parameters, parameter, values.Get(parameter));
            }
            if (modifier is MegaCrit.Sts2.Core.Models.Modifiers.SealedDeck)
                ModifierValues.SetSealedPool(modifier, entry.SealedPoolSize ?? ModifierValues.SealedPoolSpec.Default);
            if (modifier is CoopHandicap handicap)
            {
                handicap.DamageDealtPercents = entry.DamageDealtPercents ?? [];
                handicap.DamageTakenPercents = entry.DamageTakenPercents ?? [];
            }
            if (modifier is Dill dill) dill.MaxHpPerFight = entry.MaxHpPerFight ?? Dill.GrowthSpec.Default;
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

        string? validationError = null;
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
                PlaceholderText = validationError ?? (russian ? "Название набора" : "Loadout name"),
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
            if (name.Length == 0 || ModifierPresetStore.IsReservedName(name))
            {
                validationError = name.Length == 0
                    ? (russian ? "Введите название набора" : "Enter a loadout name")
                    : (russian ? "Имя Empty зарезервировано" : "Empty is reserved. Choose another name");
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

internal sealed class ModifierPresetRemoveButton : NButton
{
    // NButton._Ready rejects derived types before connecting their input signals.
    public override void _Ready() => ConnectSignals();

    public override void _GuiInput(InputEvent input)
    {
        base._GuiInput(input);
        // Keyboard/controller selection must not bubble up and also apply this row.
        if (input is InputEventMouseButton || input.IsAction(MegaCrit.Sts2.Core.ControllerInput.MegaInput.select)) AcceptEvent();
    }
}

[HarmonyPatch(typeof(NDropdown), "OpenDropdown")]
internal static class ModifierPresetDropdownFocusPatch
{
    [HarmonyPostfix]
    private static void Postfix(NDropdown __instance)
    {
        if (__instance is NActDropdown dropdown) ModifierPresetUi.RefreshDropdownFocus(dropdown);
    }
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
