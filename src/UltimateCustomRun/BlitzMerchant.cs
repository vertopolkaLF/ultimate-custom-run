using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;

namespace UltimateCustomRun;

// Shared base for the two mutually exclusive Blitz Merchant modifiers.
public abstract class BlitzMerchantModifier : ModifierModel
{
    internal const int OfferCount = 6;
    internal const int DefaultPicks = 3;
    internal static readonly ModifierValues.Spec PicksSpec = new(1, OfferCount, 1, DefaultPicks, "picks");

    protected override string IconPath => ImageHelper.GetImagePath("atlases/ui_atlas.sprites/map/icons/map_shop.tres");

    internal int Picks => ModifierValues.Get(this);

    internal static BlitzMerchantModifier? For(IRunState? state) =>
        state?.Modifiers.OfType<BlitzMerchantModifier>().FirstOrDefault();

    internal abstract bool UsesBlitzMerchant(IRunState state);
}

public sealed class BlitzMerchant : BlitzMerchantModifier
{
    internal const string DisplayTitle = "Blitz Merchant";
    internal const string DisplayDescription =
        "Every Merchant, including those found in [gold]?[/gold] rooms, is replaced by the Blitz Merchant. Choose [blue]3[/blue] of its 6 offers.";

    internal override bool UsesBlitzMerchant(IRunState state) => true;
}

public sealed class BlitzMerchantRandom : BlitzMerchantModifier
{
    internal const string DisplayTitle = "Blitz Merchant - Random";
    internal const string DisplayDescription =
        "Every Merchant, including those found in [gold]?[/gold] rooms, has a 50% chance to be the Blitz Merchant. Choose [blue]3[/blue] of its 6 offers.";

    internal override bool UsesBlitzMerchant(IRunState state) => Roll(state.Rng.Seed, state.CurrentActIndex, state.TotalFloor);

    // Derived only from shared run state, so every multiplayer peer agrees without extra messages
    // and the normal shop RNG streams are left untouched.
    internal static bool Roll(ulong seed, int actIndex, int totalFloor) =>
        new Rng(seed + (ulong)actIndex * 1_000_003UL + (ulong)totalFloor * 7_919UL, "ultimate_blitz_merchant").NextBool();
}

internal static class BlitzMerchantRoom
{
    internal static bool ShouldReplace(IRunState? state) =>
        state != null && state.GameMode != GameMode.Blitz && BlitzMerchantModifier.For(state)?.UsesBlitzMerchant(state) == true;

    internal static MerchantInventory CreateInventory(MegaCrit.Sts2.Core.Entities.Players.Player player, int picks)
    {
        var inventory = MerchantInventory.CreateForBlitzMerchant(player);
        // Native Setup only grants picks in the Blitz game mode.
        inventory.BlitzChoicesRemaining = picks;
        return inventory;
    }

    // Mirrors the Blitz branch of MerchantRoom.EnterInternal for non-Blitz runs.
    internal static async Task Enter(MerchantRoom room, IRunState state, bool isRestoringRoomStackBase, int picks)
    {
        if (isRestoringRoomStackBase)
            throw new InvalidOperationException("MerchantRoom does not support room stack reconstruction.");
        var synchronizer = RunManager.Instance.MerchantSynchronizer;
        synchronizer.OnMerchantEntered([]);
        foreach (var player in state.Players) synchronizer.SetMerchantInventory(player, CreateInventory(player, picks));
        if (!TestMode.IsOn)
        {
            var backgroundAssets = state.Act.GenerateBackgroundAssets(NCombatRoom.GenerateBackgroundRngForCurrentPoint(state));
            await PreloadManager.LoadRoomBlitzMerchantAssets(backgroundAssets, synchronizer.GetLocalInventory().BlitzEntries);
            NRun.Instance?.SetCurrentRoom(NBlitzMerchantRoom.Create(state.Players, backgroundAssets));
        }
        await Hook.AfterRoomEntered(state, room);
    }
}

// Shop map points and ? rooms that roll a shop both construct MerchantRoom.
[HarmonyPatch(typeof(MerchantRoom), nameof(MerchantRoom.EnterInternal))]
internal static class BlitzMerchantRoomPatch
{
    [HarmonyPrefix]
    private static bool Prefix(MerchantRoom __instance, IRunState? __0, bool __1, ref Task __result)
    {
        if (!BlitzMerchantRoom.ShouldReplace(__0)) return true;
        __result = BlitzMerchantRoom.Enter(__instance, __0!, __1, BlitzMerchantModifier.For(__0)!.Picks);
        return false;
    }
}
