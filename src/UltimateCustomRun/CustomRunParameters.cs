using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace UltimateCustomRun;

public sealed class CustomRunParameters : ModifierModel
{
    internal const string DisplayTitle = "Custom Run Parameters";
    internal const string DisplayDescription = "Adjust map length, boss count, starting combat resources, and health multipliers.";

    protected override string IconPath => ImageHelper.GetImagePath("packed/modifiers/specialized.png");

    // Real saved properties are discovered by the game's replay/network ID cache.
    [SavedProperty]
    public int BossesPerAct { get => Values.BossesPerAct; set => Set(CustomRunParameter.BossesPerAct, value); }
    [SavedProperty]
    public int FloorsPerAct { get => Values.FloorsPerAct; set => Set(CustomRunParameter.FloorsPerAct, value); }
    [SavedProperty]
    public int BaseHandSize { get => Values.BaseHandSize; set => Set(CustomRunParameter.BaseHandSize, value); }
    [SavedProperty]
    public int BaseEnergy { get => Values.BaseEnergy; set => Set(CustomRunParameter.BaseEnergy, value); }
    [SavedProperty]
    public int EnemyHpPercent { get => Values.EnemyHpPercent; set => Set(CustomRunParameter.EnemyHpPercent, value); }
    [SavedProperty]
    public int EnemyDamagePercent { get => Values.EnemyDamagePercent; set => Set(CustomRunParameter.EnemyDamagePercent, value); }
    [SavedProperty]
    public int PlayerHpPercent { get => Values.PlayerHpPercent; set => Set(CustomRunParameter.PlayerHpPercent, value); }

    private CustomRunParameterValues Values => CustomRunParameterValuesStore.Get(this);
    private void Set(CustomRunParameter parameter, int value) => CustomRunParameterValuesStore.Set(this, parameter, value);
}

internal enum CustomRunParameter
{
    BossesPerAct,
    FloorsPerAct,
    BaseHandSize,
    BaseEnergy,
    EnemyHpPercent,
    EnemyDamagePercent,
    PlayerHpPercent
}

internal readonly record struct CustomRunParameterValues(
    int BossesPerAct,
    int FloorsPerAct,
    int BaseHandSize,
    int BaseEnergy,
    int EnemyHpPercent,
    int EnemyDamagePercent,
    int PlayerHpPercent)
{
    internal static CustomRunParameterValues Default => new(-1, -1, -1, -1, 100, 100, 100);

    internal int Get(CustomRunParameter parameter) => parameter switch
    {
        CustomRunParameter.BossesPerAct => BossesPerAct,
        CustomRunParameter.FloorsPerAct => FloorsPerAct,
        CustomRunParameter.BaseHandSize => BaseHandSize,
        CustomRunParameter.BaseEnergy => BaseEnergy,
        CustomRunParameter.EnemyHpPercent => EnemyHpPercent,
        CustomRunParameter.EnemyDamagePercent => EnemyDamagePercent,
        CustomRunParameter.PlayerHpPercent => PlayerHpPercent,
        _ => throw new ArgumentOutOfRangeException(nameof(parameter))
    };

    internal CustomRunParameterValues With(CustomRunParameter parameter, int value) => parameter switch
    {
        CustomRunParameter.BossesPerAct => this with { BossesPerAct = value },
        CustomRunParameter.FloorsPerAct => this with { FloorsPerAct = value },
        CustomRunParameter.BaseHandSize => this with { BaseHandSize = value },
        CustomRunParameter.BaseEnergy => this with { BaseEnergy = value },
        CustomRunParameter.EnemyHpPercent => this with { EnemyHpPercent = value },
        CustomRunParameter.EnemyDamagePercent => this with { EnemyDamagePercent = value },
        CustomRunParameter.PlayerHpPercent => this with { PlayerHpPercent = value },
        _ => throw new ArgumentOutOfRangeException(nameof(parameter))
    };
}

internal static class CustomRunParameterValuesStore
{
    private sealed class State { internal CustomRunParameterValues Values = CustomRunParameterValues.Default; }
    private static readonly ConditionalWeakTable<CustomRunParameters, State> Values = new();

    internal static CustomRunParameterValues Get(CustomRunParameters modifier) =>
        Values.TryGetValue(modifier, out var state) ? state.Values : CustomRunParameterValues.Default;

    internal static CustomRunParameterValues From(IEnumerable<ModifierModel>? modifiers) =>
        modifiers?.OfType<CustomRunParameters>().FirstOrDefault() is { } modifier
            ? Get(modifier)
            : CustomRunParameterValues.Default;

    internal static void Set(CustomRunParameters modifier, CustomRunParameter parameter, int value)
    {
        modifier.AssertMutable();
        var state = Values.GetOrCreateValue(modifier);
        state.Values = state.Values.With(parameter, Normalize(parameter, value));
    }

    internal static void Copy(CustomRunParameters source, CustomRunParameters target)
    {
        target.AssertMutable();
        var values = Get(source);
        var state = Values.GetOrCreateValue(target);
        state.Values = values;
    }

    private static int Normalize(CustomRunParameter parameter, int value) => parameter switch
    {
        CustomRunParameter.BossesPerAct => value < 0 ? -1 : Math.Clamp(value, 1, 2),
        CustomRunParameter.FloorsPerAct => value < 0 ? -1 : Math.Clamp(value, 8, 30),
        CustomRunParameter.BaseHandSize => value < 0 ? -1 : Math.Clamp(value, 0, 10),
        CustomRunParameter.BaseEnergy => value < 0 ? -1 : Math.Clamp(value, 0, 10),
        _ => Math.Clamp((int)Math.Round(Math.Clamp(value, 25, 500) / 25d, MidpointRounding.AwayFromZero) * 25, 25, 500)
    };

    private static string SaveKey(CustomRunParameter parameter) =>
        nameof(CustomRunParameters) + "." + parameter;

    // Read legacy JSON saves, but never write their unregistered dotted keys.
    [HarmonyPatch(typeof(ModifierModel), nameof(ModifierModel.FromSerializable))]
    private static class LoadPatch
    {
        [HarmonyPostfix]
        private static void Postfix(SerializableModifier __0, ModifierModel __result)
        {
            if (__result is not CustomRunParameters modifier) return;
            foreach (var parameter in Enum.GetValues<CustomRunParameter>())
            {
                var key = SaveKey(parameter);
                var saved = __0.Props?.ints?.FirstOrDefault(value => value.name == key);
                if (saved is { name: var name } && name == key)
                    Set(modifier, parameter, saved.Value.value);
            }
        }
    }

    [HarmonyPatch(typeof(AbstractModel), nameof(AbstractModel.MutableClone))]
    private static class ClonePatch
    {
        [HarmonyPostfix]
        private static void Postfix(AbstractModel __instance, AbstractModel __result)
        {
            if (__instance is CustomRunParameters source && __result is CustomRunParameters target)
                Copy(source, target);
        }
    }
}
