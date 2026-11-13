using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Map;
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
        "Every Merchant, including those found in [gold]?[/gold] rooms, is replaced by the Blitz Merchant. Map Merchants sell all 6 offers for gold; in [gold]?[/gold] rooms, choose [blue]3[/blue] free offers.";

    internal override bool UsesBlitzMerchant(IRunState state) => true;
}

public sealed class BlitzMerchantRandom : BlitzMerchantModifier
{
    internal const string DisplayTitle = "Blitz Merchant - Random";
    internal const string DisplayDescription =
        "Every Merchant, including those found in [gold]?[/gold] rooms, has a 50% chance to be the Blitz Merchant. Map Merchants sell all 6 offers for gold; in [gold]?[/gold] rooms, choose [blue]3[/blue] free offers.";

    internal override bool UsesBlitzMerchant(IRunState state) => Roll(state.Rng.Seed, state.CurrentActIndex, state.TotalFloor);

    // Derived only from shared run state, so every multiplayer peer agrees without extra messages
    // and the normal shop RNG streams are left untouched.
    internal static bool Roll(ulong seed, int actIndex, int totalFloor) =>
        new Rng(seed + (ulong)actIndex * 1_000_003UL + (ulong)totalFloor * 7_919UL, "ultimate_blitz_merchant").NextBool();
}

internal static class BlitzMerchantRoom
{
    // A Merchant rolled in a ? room keeps the Unknown map point; every other Merchant is a map Merchant.
    internal static bool IsMapMerchant(IRunState state) => state.CurrentMapPoint?.PointType != MapPointType.Unknown;

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
        foreach (var player in state.Players)
        {
            var inventory = CreateInventory(player, picks);
            if (IsMapMerchant(state)) BlitzMerchantPrices.RollSale(inventory, state, player.NetId);
            synchronizer.SetMerchantInventory(player, inventory);
        }
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
        // Map Merchants charge gold instead of limiting picks; ? room Merchants are free with limited picks.
        var picks = BlitzMerchantRoom.IsMapMerchant(__0!) ? BlitzMerchantModifier.OfferCount : BlitzMerchantModifier.For(__0)!.Picks;
        __result = BlitzMerchantRoom.Enter(__instance, __0!, __1, picks);
        return false;
    }
}

// Outside Blitz a replaced map Merchant is the only gold sink, so its offers cost gold. ? room Merchants stay free.
// Base prices follow the normal Merchant: cards 50/75/150 by rarity (Colorless +15%), relics 175-275,
// potions 50-100, and card removal 75 + 25 per removal. Cursed offers are discounted for their Curse.
internal static class BlitzMerchantPrices
{
    internal static bool IsActive(IRunState state) =>
        state.GameMode != GameMode.Blitz && BlitzMerchantModifier.For(state) != null && BlitzMerchantRoom.IsMapMerchant(state);

    internal static int BasePrice(BlitzMerchantEntryType type, Player player) => type switch
    {
        BlitzMerchantEntryType.CardUpgrade => 75,
        BlitzMerchantEntryType.UpgradedCardReward => 100,
        BlitzMerchantEntryType.RandomUpgrades => 100,
        BlitzMerchantEntryType.CardTransform => 75,
        BlitzMerchantEntryType.CardRemoval => RemovalPrice(player.ExtraFields.CardShopRemovalsUsed),
        BlitzMerchantEntryType.CardRewards => 125,
        BlitzMerchantEntryType.ColorlessCardReward => 85,
        BlitzMerchantEntryType.Potions => 125,
        BlitzMerchantEntryType.CardEnchant => 100,
        BlitzMerchantEntryType.EnchantedCardReward => 100,
        BlitzMerchantEntryType.PrismaticCardReward => 125,
        BlitzMerchantEntryType.RareCardReward => 150,
        BlitzMerchantEntryType.UpgradedRareCard => 150,
        BlitzMerchantEntryType.Relic => 200,
        BlitzMerchantEntryType.Glam => 75,
        BlitzMerchantEntryType.TwoMoreChoices => 50,
        BlitzMerchantEntryType.CardRewardOfEachRarity => 175,
        BlitzMerchantEntryType.UpgradedRareCardReward => 150,
        BlitzMerchantEntryType.TwoRelics => 300,
        BlitzMerchantEntryType.FullHealAndMaxHp => 100,
        _ => 100
    };

    // Same formula as the normal Merchant's card removal, including Inflation.
    internal static int RemovalPrice(int removalsUsed) =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.Inflation, 100, 75) +
        AscensionHelper.GetValueIfAscension(AscensionLevel.Inflation, 50, 25) * removalsUsed;

    internal const int MinSalePercent = 15;
    internal const int MaxSalePercent = 50;

    private sealed class Sale { internal required int Percent; }
    private static readonly ConditionalWeakTable<BlitzMerchantEntry, Sale> Sales = new();

    internal static int? SalePercent(BlitzMerchantEntry entry) => Sales.TryGetValue(entry, out var sale) ? sale.Percent : null;

    // One random offer per map Merchant is on sale. The roll uses only shared run state and the owner's id,
    // so every co-op peer computes the same sale without touching the native shop RNG.
    internal static (int Index, int Percent) RollSale(ulong seed, int actIndex, int totalFloor, ulong playerId, int offerCount)
    {
        var rng = new Rng(seed + (ulong)actIndex * 1_000_003UL + (ulong)totalFloor * 7_919UL + playerId * 104_729UL,
            "ultimate_blitz_merchant_sale");
        return (rng.NextInt(offerCount), rng.NextInt(MinSalePercent, MaxSalePercent + 1));
    }

    internal static void RollSale(MerchantInventory inventory, IRunState state, ulong playerId)
    {
        if (inventory.BlitzEntries.Count == 0) return;
        var (index, percent) = RollSale(state.Rng.Seed, state.CurrentActIndex, state.TotalFloor, playerId, inventory.BlitzEntries.Count);
        Sales.AddOrUpdate(inventory.BlitzEntries[index], new Sale { Percent = percent });
    }

    internal static int Price(BlitzMerchantEntry entry)
    {
        var player = entry.Player;
        var price = (decimal)BasePrice(entry.MerchantEntryType, player);
        if (Sales.TryGetValue(entry, out var sale)) price = Math.Round(price * (100 - sale.Percent) / 100m);
        // Native discounts and surcharges, such as Membership Card, apply as at the normal Merchant.
        if (player.RunState.CurrentRoom is MerchantRoom)
            price = Hook.ModifyMerchantPrice(player.RunState, player, entry, price);
        return Math.Max(0, (int)price);
    }
}

[HarmonyPatch(typeof(BlitzMerchantEntry), nameof(BlitzMerchantEntry.Cost), MethodType.Getter)]
internal static class BlitzMerchantCostPatch
{
    [HarmonyPostfix]
    private static void Postfix(BlitzMerchantEntry __instance, ref int __result)
    {
        if (BlitzMerchantPrices.IsActive(__instance.Player.RunState)) __result = BlitzMerchantPrices.Price(__instance);
    }
}

// The base purchase flow already refuses offers the player cannot afford; this charges for accepted ones.
[HarmonyPatch(typeof(BlitzMerchantEntry), "OnTryPurchaseInternal")]
internal static class BlitzMerchantPurchasePatch
{
    [HarmonyPrefix]
    private static void Prefix(BlitzMerchantEntry __instance, out int __state) =>
        // Card removal raises its own future price during selection, so read the price first.
        __state = BlitzMerchantPrices.IsActive(__instance.Player.RunState) ? __instance.Cost : 0;

    [HarmonyPostfix]
    private static void Postfix(BlitzMerchantEntry __instance, bool __1, int __state, ref Task<(bool, int)> __result)
    {
        if (__state > 0 && !__1) __result = Charge(__result, __instance.Player, __state);
    }

    internal static async Task<(bool, int)> Charge(Task<(bool, int)> purchase, Player player, int price)
    {
        var (success, _) = await purchase;
        if (!success) return (false, 0);
        await PlayerCmd.LoseGold(price, player, GoldLossType.Spent);
        return (true, price);
    }
}

[HarmonyPatch(typeof(NBlitzMerchantSlot), "UpdateVisual")]
internal static class BlitzMerchantSlotPricePatch
{
    [HarmonyPostfix]
    private static void Postfix(NBlitzMerchantSlot __instance, MegaLabel ____costLabel)
    {
        if (__instance.Entry is not BlitzMerchantEntry entry || !BlitzMerchantPrices.IsActive(entry.Player.RunState)) return;
        var available = entry.Status == BlitzMerchantEntry.BlitzEntryStatus.Available;
        ____costLabel.Visible = available;
        if (!available) return;
        var sale = BlitzMerchantPrices.SalePercent(entry);
        ____costLabel.SetTextAutoSize(entry.Cost.ToString());
        ____costLabel.Modulate = !entry.EnoughGold ? StsColors.red : sale != null ? StsColors.green : StsColors.cream;
        // The Blitz slot scene may hide the price container because native Blitz offers are free.
        for (var node = ____costLabel.GetParent(); node != null && node != __instance; node = node.GetParent())
            if (node is Godot.CanvasItem item) item.Visible = true;
    }
}
