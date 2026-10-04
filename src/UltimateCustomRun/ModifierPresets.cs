using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Models;
using GameFileAccess = Godot.FileAccess;

namespace UltimateCustomRun;

internal sealed class ModifierPreset
{
    public string Name { get; set; } = string.Empty;
    public List<ModifierPresetEntry> Modifiers { get; set; } = [];
}

internal sealed class ModifierPresetEntry
{
    public string Id { get; set; } = string.Empty;
    public int? Value { get; set; }
    public int? SealedPoolSize { get; set; }
    public int? MaxHpPerFight { get; set; }
    public CustomRunParameterValues? Parameters { get; set; }
}

internal static class ModifierPresetStore
{
    private const string FilePath = "user://ultimate_custom_run_presets.json";
    internal const string EmptyName = "Empty";
    internal static ModifierPreset Empty => new() { Name = EmptyName };
    internal static bool IsReservedName(string name) =>
        string.Equals(name.Trim(), EmptyName, StringComparison.OrdinalIgnoreCase);

    internal static List<ModifierPreset> Load()
    {
        try { return Read(); }
        catch (Exception ex)
        {
            GD.PushWarning($"[Ultimate Custom Run] Could not read modifier presets: {ex.Message}");
            return [];
        }
    }

    internal static void Save(string name, IEnumerable<ModifierModel> modifiers)
    {
        var preset = Capture(name, modifiers);
        var presets = Read();
        var existing = presets.FindIndex(item => string.Equals(item.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0) presets[existing] = preset;
        else presets.Add(preset);

        Write(presets);
    }

    internal static ModifierPreset Capture(string name, IEnumerable<ModifierModel> modifiers)
    {
        if (string.IsNullOrWhiteSpace(name) || IsReservedName(name))
            throw new ArgumentException("Choose a non-empty preset name other than Empty.", nameof(name));
        var preset = new ModifierPreset { Name = name.Trim() };
        foreach (var modifier in modifiers)
        {
            var entry = new ModifierPresetEntry { Id = modifier.Id.ToString() };
            if (ModifierValues.For(modifier) != null) entry.Value = ModifierValues.Get(modifier);
            if (modifier is MegaCrit.Sts2.Core.Models.Modifiers.SealedDeck)
                entry.SealedPoolSize = ModifierValues.GetSealedPool(modifier);
            if (modifier is Dill dill) entry.MaxHpPerFight = dill.MaxHpPerFight;
            if (modifier is CustomRunParameters parameters)
                entry.Parameters = CustomRunParameterValuesStore.Get(parameters);
            preset.Modifiers.Add(entry);
        }

        return preset;
    }

    internal static void Delete(string name)
    {
        var presets = Read();
        if (Remove(presets, name)) Write(presets);
    }

    internal static bool Remove(List<ModifierPreset> presets, string name) =>
        presets.RemoveAll(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)) > 0;

    private static void Write(List<ModifierPreset> presets)
    {
        var file = GameFileAccess.Open(FilePath, GameFileAccess.ModeFlags.Write)
            ?? throw new IOException($"Could not open {FilePath} for writing: {GameFileAccess.GetOpenError()}");
        try { file.StoreString(JsonSerializer.Serialize(presets)); }
        finally { file.Close(); }
    }

    private static List<ModifierPreset> Read()
    {
        if (!GameFileAccess.FileExists(FilePath)) return [];
        var file = GameFileAccess.Open(FilePath, GameFileAccess.ModeFlags.Read);
        if (file == null) return [];
        string json;
        try { json = file.GetAsText(); }
        finally { file.Close(); }

        return JsonSerializer.Deserialize<List<ModifierPreset>>(json) ?? [];
    }
}
