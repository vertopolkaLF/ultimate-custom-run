using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using GameFileAccess = Godot.FileAccess;

namespace UltimateCustomRun;

internal sealed class AdvancedModeSettings
{
    public bool Enabled { get; set; }
}

// Advanced mode reveals Run Parameters, value sliders and the wildest modifiers.
// Without it the list stays close to vanilla: those modifiers are hidden and every value uses its default.
internal static class AdvancedMode
{
    private const string FilePath = "user://ultimate_custom_run_options.json";
    private static AdvancedModeSettings? settings;
    private static bool persist = true;

    internal static AdvancedModeSettings Settings => settings ??= Load();

    internal static bool Enabled
    {
        get => Settings.Enabled;
        set
        {
            if (Settings.Enabled == value) return;
            Settings.Enabled = value;
            Save();
        }
    }

    internal static bool IsAdvancedModifier(ModifierModel? modifier) =>
        modifier is CustomRunParameters or Headstart or TwentyTwentyModifier or SuperDraft or UltimateStarter
            or NoStarterDeck or Dill or CoopHandicap;

    internal static bool HasCustomValue(ModifierModel modifier)
    {
        if (modifier is SealedDeck && ModifierValues.GetSealedPool(modifier) != ModifierValues.SealedPoolSpec.Default) return true;
        return ModifierValues.For(modifier) is { } spec && ModifierValues.Get(modifier) != spec.Default;
    }

    internal static bool Uses(IEnumerable<ModifierModel> modifiers) =>
        modifiers.Any(modifier => IsAdvancedModifier(modifier) || HasCustomValue(modifier));

    internal static void ResetValues(ModifierModel modifier)
    {
        // The Sealed Deck pick limit depends on the pool, so restore the pool first.
        if (modifier is SealedDeck) ModifierValues.SetSealedPool(modifier, ModifierValues.SealedPoolSpec.Default);
        if (ModifierValues.For(modifier) is { } spec) ModifierValues.Set(modifier, spec.Default);
    }

    private static List<NRunModifierTickbox> Tickboxes(NCustomRunModifiersList list) =>
        (List<NRunModifierTickbox>)AccessTools.Field(typeof(NCustomRunModifiersList), "_modifierTickboxes").GetValue(list)!;

    internal static bool IsEditable(NCustomRunModifiersList list) =>
        (MultiplayerUiMode)AccessTools.Field(typeof(NCustomRunModifiersList), "_mode").GetValue(list)!
            is MultiplayerUiMode.Singleplayer or MultiplayerUiMode.Host;

    // Clients always see what the host selected, regardless of their own setting.
    internal static bool IsActive(NCustomRunModifiersList list) =>
        (IsEditable(list) && Enabled) ||
        Uses(Tickboxes(list).Where(row => row.IsTicked && row.Modifier != null).Select(row => row.Modifier!));

    // Turning the mode off drops everything it would hide, so hidden state never reaches the run, presets or the lobby.
    internal static void SetEnabled(NCustomRunModifiersList list, bool enabled)
    {
        Enabled = enabled;
        if (!enabled && IsEditable(list))
            foreach (var row in Tickboxes(list))
            {
                if (row.Modifier is not { } modifier) continue;
                if (IsAdvancedModifier(modifier)) row.IsTicked = false;
                ResetValues(modifier);
            }
        ModifierVariantUi.RefreshFromTickboxes(list);
        ModifierGroupsUi.ApplyMode(list);
        ModifierValueUi.Refresh(list);
        if (IsEditable(list)) list.EmitSignal(NCustomRunModifiersList.SignalName.ModifiersChanged);
    }

    // A preset or roll that needs advanced options switches the mode on instead of silently dropping them.
    internal static void BeforeIncoming(NCustomRunModifiersList list, IReadOnlyCollection<ModifierModel> modifiers)
    {
        if (!IsEditable(list) || Enabled || !Uses(modifiers)) return;
        Enabled = true;
        ModifierGroupsUi.RefreshAdvancedToggle(list);
    }

    // Headless tests have no Godot file system.
    internal static void UseInMemorySettings(bool enabled)
    {
        settings = new AdvancedModeSettings { Enabled = enabled };
        persist = false;
    }

    private static void Save()
    {
        if (!persist) return;
        try
        {
            var file = GameFileAccess.Open(FilePath, GameFileAccess.ModeFlags.Write)
                ?? throw new IOException($"Could not open {FilePath} for writing: {GameFileAccess.GetOpenError()}");
            try { file.StoreString(JsonSerializer.Serialize(Settings)); }
            finally { file.Close(); }
        }
        catch (Exception ex) { GD.PushWarning($"[Ultimate Custom Run] Could not save options: {ex.Message}"); }
    }

    private static AdvancedModeSettings Load()
    {
        try
        {
            if (!GameFileAccess.FileExists(FilePath)) return new AdvancedModeSettings();
            var file = GameFileAccess.Open(FilePath, GameFileAccess.ModeFlags.Read);
            if (file == null) return new AdvancedModeSettings();
            string json;
            try { json = file.GetAsText(); }
            finally { file.Close(); }
            return JsonSerializer.Deserialize<AdvancedModeSettings>(json) ?? new AdvancedModeSettings();
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[Ultimate Custom Run] Could not read options: {ex.Message}");
            return new AdvancedModeSettings();
        }
    }
}
