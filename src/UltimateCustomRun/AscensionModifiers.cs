using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;

namespace UltimateCustomRun;

public abstract class AscensionModifier : ModifierModel
{
    public abstract AscensionLevel Level { get; }
    public override LocString Title => AscensionHelper.GetTitle((int)Level);
    public override LocString Description => AscensionHelper.GetDescription((int)Level);
    protected override string IconPath => ImageHelper.GetImagePath("packed/modifiers/murderous.png");
}

public sealed class SwarmingElites : AscensionModifier { public override AscensionLevel Level => AscensionLevel.SwarmingElites; }
public sealed class WearyTraveler : AscensionModifier { public override AscensionLevel Level => AscensionLevel.WearyTraveler; }
public sealed class Poverty : AscensionModifier { public override AscensionLevel Level => AscensionLevel.Poverty; }
public sealed class TightBelt : AscensionModifier { public override AscensionLevel Level => AscensionLevel.TightBelt; }
public sealed class AscendersBane : AscensionModifier { public override AscensionLevel Level => AscensionLevel.AscendersBane; }
public sealed class Inflation : AscensionModifier { public override AscensionLevel Level => AscensionLevel.Inflation; }
public sealed class Scarcity : AscensionModifier { public override AscensionLevel Level => AscensionLevel.Scarcity; }
public sealed class ToughEnemies : AscensionModifier { public override AscensionLevel Level => AscensionLevel.ToughEnemies; }
public sealed class DeadlyEnemies : AscensionModifier { public override AscensionLevel Level => AscensionLevel.DeadlyEnemies; }
public sealed class DoubleBoss : AscensionModifier { public override AscensionLevel Level => AscensionLevel.DoubleBoss; }

internal static class AscensionModifiers
{
    internal static readonly Type[] Types = [typeof(SwarmingElites), typeof(WearyTraveler), typeof(Poverty),
        typeof(TightBelt), typeof(AscendersBane), typeof(Inflation), typeof(Scarcity), typeof(ToughEnemies),
        typeof(DeadlyEnemies), typeof(DoubleBoss)];

    internal static IEnumerable<ModifierModel> Create() => Types.Select(type => ModelDb.GetById<ModifierModel>(ModelDb.GetId(type)).ToMutable());

    internal static bool HasLevel(IEnumerable<ModifierModel> modifiers, AscensionLevel level) =>
        modifiers.OfType<AscensionModifier>().Any(modifier => modifier.Level == level);

    internal static List<ModifierModel> WithoutAscensions(IEnumerable<ModifierModel> modifiers) =>
        modifiers.Where(modifier => modifier is not AscensionModifier).ToList();
}

[HarmonyPatch(typeof(AscensionManager), nameof(AscensionManager.HasLevel))]
internal static class IndependentAscensionPatch
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AscensionManager, RunState> Runs = new();

    internal static void Bind(AscensionManager manager, RunState state) => Runs.AddOrUpdate(manager, state);

    [HarmonyPostfix]
    private static void Postfix(AscensionManager __instance, AscensionLevel __0, ref bool __result)
    {
        // Only the active run's manager receives its modifiers; unrelated managers retain vanilla behavior.
        if (!__result && Runs.TryGetValue(__instance, out var state))
            __result = AscensionModifiers.HasLevel(state.Modifiers, __0);
    }
}

[HarmonyPatch(typeof(RunManager), "InitializeShared")]
internal static class AscensionRunBindingPatch
{
    [HarmonyPostfix]
    private static void Postfix(RunManager __instance)
    {
        var state = (RunState)AccessTools.Field(typeof(RunManager), "<State>k__BackingField").GetValue(__instance)!;
        IndependentAscensionPatch.Bind(__instance.AscensionManager, state);
    }
}

[HarmonyPatch(typeof(NCustomRunScreen), "OnModifiersListChanged")]
internal static class AscensionModifierSelectionPatch
{
    [HarmonyPrefix]
    private static void Prefix(NCustomRunScreen __instance)
    {
        if (__instance.Lobby.NetService.Type == NetGameType.Client) return;
        var list = (NCustomRunModifiersList)AccessTools.Field(typeof(NCustomRunScreen), "_modifiersList").GetValue(__instance)!;
        if (list.GetModifiersTickedOn().Any(modifier => modifier is AscensionModifier))
            __instance.Lobby.SyncAscensionChange(0);
    }
}

[HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen.AscensionChanged))]
internal static class AscensionLevelSelectionPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCustomRunScreen __instance)
    {
        // Resetting to zero for an independent modifier must not erase the newly selected effects.
        if (__instance.Lobby.NetService.Type == NetGameType.Client || __instance.Lobby.Ascension == 0) return;
        var list = (NCustomRunModifiersList)AccessTools.Field(typeof(NCustomRunScreen), "_modifiersList").GetValue(__instance)!;
        var selected = list.GetModifiersTickedOn();
        if (selected.Any(modifier => modifier is AscensionModifier))
            list.SetTickedModifiers(AscensionModifiers.WithoutAscensions(selected));
    }
}
