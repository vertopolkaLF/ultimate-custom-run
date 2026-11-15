using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Modifiers;
using GameFileAccess = Godot.FileAccess;

namespace UltimateCustomRun;

internal sealed class RandomizerSettings
{
    public Dictionary<string, int[]> Ranges { get; set; } = [];
    public bool Character { get; set; } = true;
    public bool Values { get; set; }
    public bool Expanded { get; set; }

    internal (int Min, int Max) Range(ModifierGroup group)
    {
        if (Ranges.TryGetValue(group.ToString(), out var range) && range.Length == 2)
            return (Math.Max(0, Math.Min(range[0], range[1])), Math.Max(0, Math.Max(range[0], range[1])));
        return ModifierRandomizer.DefaultRange(group);
    }

    internal void SetRange(ModifierGroup group, int min, int max) =>
        Ranges[group.ToString()] = [Math.Max(0, Math.Min(min, max)), Math.Max(0, Math.Max(min, max))];
}

// Pure roll logic: chooses modifiers by group ranges and rolls their configurable values.
internal static class ModifierRandomizer
{
    private const string FilePath = "user://ultimate_custom_run_randomizer.json";
    private static RandomizerSettings? settings;

    // Groups that the roll fills, in display order. Randomizer and Disabled hold no rollable modifiers.
    internal static IEnumerable<ModifierGroup> RollGroups =>
        ModifierGroups.Sections.Select(section => section.Group)
            .Where(group => group is not (ModifierGroup.Randomizer or ModifierGroup.Disabled));

    // Roughly the vanilla randomizer (two good modifiers and one bad), plus an optional starting deck.
    internal static (int Min, int Max) DefaultRange(ModifierGroup group) => group switch
    {
        ModifierGroup.ImprovedStart => (0, 1),
        ModifierGroup.Modifiers => (1, 2),
        ModifierGroup.Negatives => (1, 1),
        _ => (0, 0)
    };

    internal static RandomizerSettings Settings => settings ??= Load();

    internal static List<ModifierModel> Pick(
        IReadOnlyDictionary<ModifierGroup, IReadOnlyList<ModifierModel>> candidates,
        Func<ModifierGroup, (int Min, int Max)> range, Random rng)
    {
        var picked = new List<ModifierModel>();
        foreach (var group in RollGroups)
        {
            if (!candidates.TryGetValue(group, out var pool) || pool.Count == 0) continue;
            var (min, max) = range(group);
            max = Math.Clamp(max, 0, pool.Count);
            min = Math.Clamp(min, 0, max);
            var target = rng.Next(min, max + 1);
            var count = 0;
            foreach (var modifier in pool.OrderBy(_ => rng.Next()).ToList())
            {
                if (count >= target) break;
                // Mutually exclusive choices are skipped, so a range may end below its minimum.
                if (picked.Any(other => LinkedModifierChains.AreLinked(other, modifier))) continue;
                picked.Add(modifier);
                count++;
            }
        }
        return picked;
    }

    internal static (int Min, int Max) RandomRange(int available, Random rng)
    {
        var first = rng.Next(available + 1);
        var second = rng.Next(available + 1);
        return (Math.Min(first, second), Math.Max(first, second));
    }

    internal static int RandomIn(ModifierValues.Spec spec, Random rng) =>
        spec.Max < spec.Min ? spec.Default : spec.Min + rng.Next((spec.Max - spec.Min) / spec.Step + 1) * spec.Step;

    // Co-op Handicap keeps its per-player values; a random damage split is not a run modifier choice.
    internal static void RandomizeValues(ModifierModel modifier, Random rng, bool blitz)
    {
        if (modifier is SealedDeck) ModifierValues.SetSealedPool(modifier, RandomIn(ModifierValues.SealedPoolSpec, rng));
        if (ModifierValues.For(modifier) is { } spec) ModifierValues.Set(modifier, RandomIn(spec, rng));
        if (modifier is Dill dill) dill.MaxHpPerFight = RandomIn(Dill.GrowthSpec, rng);
        if (modifier is CustomRunParameters parameters)
            foreach (var parameter in Enum.GetValues<CustomRunParameter>())
            {
                if (blitz && BlitzCompatibility.IsParameterDisabled(parameter)) continue;
                CustomRunParameterValuesStore.Set(parameters, parameter, CustomRunParametersUi.RandomValue(parameter, rng));
            }
    }

    internal static void Save()
    {
        try
        {
            var file = GameFileAccess.Open(FilePath, GameFileAccess.ModeFlags.Write)
                ?? throw new IOException($"Could not open {FilePath} for writing: {GameFileAccess.GetOpenError()}");
            try { file.StoreString(JsonSerializer.Serialize(Settings)); }
            finally { file.Close(); }
        }
        catch (Exception ex) { GD.PushWarning($"[Ultimate Custom Run] Could not save randomizer settings: {ex.Message}"); }
    }

    private static RandomizerSettings Load()
    {
        try
        {
            if (!GameFileAccess.FileExists(FilePath)) return new RandomizerSettings();
            var file = GameFileAccess.Open(FilePath, GameFileAccess.ModeFlags.Read);
            if (file == null) return new RandomizerSettings();
            string json;
            try { json = file.GetAsText(); }
            finally { file.Close(); }
            return JsonSerializer.Deserialize<RandomizerSettings>(json) ?? new RandomizerSettings();
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[Ultimate Custom Run] Could not read randomizer settings: {ex.Message}");
            return new RandomizerSettings();
        }
    }
}
