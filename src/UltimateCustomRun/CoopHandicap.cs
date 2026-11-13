using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace UltimateCustomRun;

internal enum HandicapStat { DamageDealt, DamageTaken }

public sealed class CoopHandicap : ModifierModel
{
    internal const int MaxPlayers = 4;
    internal const string DisplayTitle = "Handicap";
    internal const string DisplayDescription = "Set the damage each player deals and takes.";
    internal static readonly ModifierValues.Spec PercentSpec = new(25, 500, 25, 100, "%");

    private int[] _damageDealt = Defaults();
    private int[] _damageTaken = Defaults();

    protected override string IconPath => ImageHelper.GetImagePath("packed/modifiers/specialized.png");

    // Indexed by the run's player slot (lobby join order). Real saved properties are networked to clients.
    [SavedProperty]
    public int[] DamageDealtPercents
    {
        get => (int[])_damageDealt.Clone();
        set { AssertMutable(); _damageDealt = Normalize(value); }
    }

    [SavedProperty]
    public int[] DamageTakenPercents
    {
        get => (int[])_damageTaken.Clone();
        set { AssertMutable(); _damageTaken = Normalize(value); }
    }

    internal int Get(HandicapStat stat, int slot) =>
        slot is >= 0 and < MaxPlayers ? Values(stat)[slot] : PercentSpec.Default;

    internal void Set(HandicapStat stat, int slot, int value)
    {
        AssertMutable();
        if (slot is < 0 or >= MaxPlayers) throw new ArgumentOutOfRangeException(nameof(slot));
        Values(stat)[slot] = PercentSpec.Normalize(value);
    }

    internal void CopyFrom(CoopHandicap source)
    {
        AssertMutable();
        _damageDealt = (int[])source._damageDealt.Clone();
        _damageTaken = (int[])source._damageTaken.Clone();
    }

    internal bool IsDefault => _damageDealt.Concat(_damageTaken).All(value => value == PercentSpec.Default);

    private int[] Values(HandicapStat stat) => stat == HandicapStat.DamageDealt ? _damageDealt : _damageTaken;

    protected override void DeepCloneFields()
    {
        base.DeepCloneFields();
        _damageDealt = (int[])_damageDealt.Clone();
        _damageTaken = (int[])_damageTaken.Clone();
    }

    private static int[] Defaults() => Enumerable.Repeat(PercentSpec.Default, MaxPlayers).ToArray();

    private static int[] Normalize(int[]? values) => Enumerable.Range(0, MaxPlayers)
        .Select(slot => values != null && slot < values.Length ? PercentSpec.Normalize(values[slot]) : PercentSpec.Default)
        .ToArray();

    internal string DescriptionText(bool russian)
    {
        var lines = Enumerable.Range(0, MaxPlayers)
            .Where(slot => Get(HandicapStat.DamageDealt, slot) != 100 || Get(HandicapStat.DamageTaken, slot) != 100)
            .Select(slot => (russian ? "Игрок " : "Player ") + (slot + 1) + ": " +
                (russian ? "наносит " : "deals ") + $"[gold]{Get(HandicapStat.DamageDealt, slot)}%[/gold], " +
                (russian ? "получает " : "takes ") + $"[gold]{Get(HandicapStat.DamageTaken, slot)}%[/gold]");
        var text = string.Join("\n", lines);
        if (text.Length > 0) return text;
        return russian ? "Урон каждого игрока по умолчанию." : DisplayDescription;
    }

    /// <summary>Run slot of the player that controls a creature, including pets such as Osty.</summary>
    internal static int SlotOf(IRunState runState, Creature? creature)
    {
        if ((creature?.Player ?? creature?.PetOwner) is not { } owner) return -1;
        var players = runState.Players;
        for (var i = 0; i < players.Count; i++)
            if (ReferenceEquals(players[i], owner)) return i;
        return -1;
    }
}

[HarmonyPatch]
internal static class CoopHandicapDamagePatch
{
    private static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(Hook), "ModifyDamage")
        ?? throw new MissingMethodException(typeof(Hook).FullName, "ModifyDamage");

    [HarmonyPostfix]
    internal static void Postfix(IRunState? runState, Creature? dealer, Creature? target, ref decimal __result)
    {
        if (runState?.Modifiers.OfType<CoopHandicap>().FirstOrDefault() is not { } modifier) return;
        // Previews may have no target. Friendly fire and self-damage are never scaled.
        var dealerSlot = CoopHandicap.SlotOf(runState, dealer);
        if (dealerSlot >= 0 && (target == null || target.Side != CombatSide.Player))
            __result *= modifier.Get(HandicapStat.DamageDealt, dealerSlot) / 100m;
        var targetSlot = CoopHandicap.SlotOf(runState, target);
        if (targetSlot >= 0 && (dealer == null || dealer.Side != CombatSide.Player))
            __result *= modifier.Get(HandicapStat.DamageTaken, targetSlot) / 100m;
    }
}
