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

// Advanced mode ("Unbalanced") raises every value slider's upper limit far past its normal range.
// Without it the sliders, Run Parameters and Handicap stay available within their normal limits.
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

    // A value above its normal slider limit only exists in Unbalanced mode.
    internal static bool NeedsUnbalanced(ModifierModel modifier)
    {
        if (modifier is SealedDeck && ModifierValues.SealedPoolSpec.Exceeds(ModifierValues.GetSealedPool(modifier))) return true;
        if (modifier is Dill dill && Dill.GrowthSpec.Exceeds(dill.MaxHpPerFight)) return true;
        if (modifier is CustomRunParameters parameters &&
            CustomRunParametersUi.NeedsUnbalanced(CustomRunParameterValuesStore.Get(parameters))) return true;
        if (modifier is CoopHandicap { NeedsUnbalanced: true }) return true;
        return ModifierValues.For(modifier) is { } spec && spec.Exceeds(ModifierValues.Get(modifier));
    }

    internal static bool Uses(IEnumerable<ModifierModel> modifiers) => modifiers.Any(NeedsUnbalanced);

    internal static void ClampToBalanced(ModifierModel modifier)
    {
        // The Sealed Deck pick limit depends on the pool, so clamp the pool first.
        if (modifier is SealedDeck)
            ModifierValues.SetSealedPool(modifier, Math.Min(ModifierValues.GetSealedPool(modifier), ModifierValues.SealedPoolSpec.NormalMax));
        if (ModifierValues.For(modifier) is { } spec && spec.Exceeds(ModifierValues.Get(modifier)))
            ModifierValues.Set(modifier, spec.NormalMax);
        if (modifier is Dill dill) dill.MaxHpPerFight = Math.Min(dill.MaxHpPerFight, Dill.GrowthSpec.NormalMax);
        if (modifier is CustomRunParameters parameters) CustomRunParametersUi.ClampToBalanced(parameters);
        if (modifier is CoopHandicap handicap) handicap.ClampToBalanced();
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

    // Turning the mode off pulls every value back inside its normal limit, so the sliders always show the real value.
    internal static void SetEnabled(NCustomRunModifiersList list, bool enabled)
    {
        Enabled = enabled;
        if (!enabled && IsEditable(list))
            foreach (var row in Tickboxes(list))
                if (row.Modifier is { } modifier) ClampToBalanced(modifier);
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
