using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace UltimateCustomRun;

public sealed class Dill : ModifierModel
{
    internal const string DisplayTitle = "Dill";
    internal const string DisplayDescription = "Start the game with [blue]1[/blue] max HP. Each fight increases max HP by [blue]2[/blue].";
    internal static readonly ModifierValues.Spec GrowthSpec = new(1, 5, 1, 2, "max HP per fight");
    private static readonly MethodInfo SetMaxHp = AccessTools.PropertySetter(typeof(Creature), nameof(Creature.MaxHp));
    private static readonly MethodInfo SetCurrentHp = AccessTools.PropertySetter(typeof(Creature), nameof(Creature.CurrentHp));
    private int _maxHpPerFight = 2;

    [SavedProperty]
    public int MaxHpPerFight
    {
        get => _maxHpPerFight;
        set { AssertMutable(); _maxHpPerFight = GrowthSpec.Normalize(value); }
    }

    protected override string IconPath => ImageHelper.GetImagePath("packed/modifiers/terminal.png");

    internal static void InitializeHealth(RunState run)
    {
        if (run.Modifiers.OfType<Dill>().FirstOrDefault() is not { } modifier) return;
        var initialHp = ModifierValues.Get(modifier);
        foreach (var player in run.Players)
        {
            SetMaxHp.Invoke(player.Creature, [initialHp]);
            SetCurrentHp.Invoke(player.Creature, [initialHp]);
        }
    }

    public override Task AfterCombatVictory(CombatRoom room) => GrowAfterVictory(CreatureCmd.GainMaxHp);

    internal async Task GrowAfterVictory(Func<Creature, decimal, Task> gainMaxHp)
    {
        foreach (var player in RunState.Players.Where(player => !player.Creature.IsDead))
            await gainMaxHp(player.Creature, MaxHpPerFight);
    }
}

// Apply the exact initial HP after native Ascension and custom HP scaling.
// Saved-run initialization is deliberately not patched: earned max HP must persist.
[HarmonyPatch(typeof(RunManager), "InitializeNewRun")]
internal static class DillStartingHealthPatch
{
    [HarmonyPostfix]
    private static void Postfix(RunManager __instance)
    {
        if (__instance.DebugOnlyGetState() is { } run) Dill.InitializeHealth(run);
    }
}
