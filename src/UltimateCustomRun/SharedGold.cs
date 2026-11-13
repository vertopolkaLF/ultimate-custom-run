using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace UltimateCustomRun;

public sealed class SharedGold : ModifierModel
{
    internal const string DisplayTitle = "Shared Gold";
    internal const string DisplayDescription =
        "All players share one wallet. It starts with everyone's starting [gold]Gold[/gold] combined, and every player's income and spending use it.";

    protected override string IconPath => ImageHelper.GetImagePath("packed/modifiers/midas.png");

    // Not saved: each peer enables mirroring after its own deterministic run setup or load.
    internal bool IsPoolActive { get; private set; }

    protected override void AfterRunLoaded(RunState runState) => IsPoolActive = true;

    internal static void InitializePool(IRunState run)
    {
        if (run.Modifiers.OfType<SharedGold>().FirstOrDefault() is not { } modifier) return;
        var total = run.Players.Sum(player => player.Gold);
        modifier.IsPoolActive = true;
        if (run.Players.Count > 0) run.Players[0].Gold = total;
    }

    internal static void Mirror(Player source)
    {
        if (source.RunState is not { } run ||
            run.Modifiers.OfType<SharedGold>().FirstOrDefault() is not { IsPoolActive: true }) return;
        foreach (var player in run.Players)
            if (!ReferenceEquals(player, source)) player.Gold = source.Gold;
    }
}

// Start the pool after native setup, so per-player starting gold is final before it is combined.
[HarmonyPatch(typeof(RunManager), "InitializeNewRun")]
internal static class SharedGoldStartPatch
{
    [HarmonyPostfix]
    private static void Postfix(RunManager __instance)
    {
        if (__instance.DebugOnlyGetState() is { } run) SharedGold.InitializePool(run);
    }
}

// Every gold change runs on every peer through the same synchronized command, so mirroring it
// into the other players' wallets stays deterministic and keeps native history, hooks and UI events.
[HarmonyPatch(typeof(Player), nameof(Player.Gold), MethodType.Setter)]
internal static class SharedGoldMirrorPatch
{
    [ThreadStatic] private static bool _mirroring;

    [HarmonyPostfix]
    internal static void Postfix(Player __instance)
    {
        if (_mirroring) return;
        _mirroring = true;
        try { SharedGold.Mirror(__instance); }
        finally { _mirroring = false; }
    }
}
