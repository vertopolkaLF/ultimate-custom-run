using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;

namespace UltimateCustomRun;

public sealed class RichLoot : ModifierModel
{
    internal const string DisplayTitle = "Rich Loot";
    internal const string DisplayDescription = "Treasure chests contain [blue]1[/blue] extra relic.";
    protected override string IconPath => ImageHelper.GetImagePath("packed/modifiers/midas.png");

    internal static bool IsActive(IRunState runState) =>
        runState.Modifiers.Any(modifier => modifier is RichLoot);
}

[HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.BeginRelicPicking))]
internal static class RichLootChestPatch
{
    [HarmonyPostfix]
    private static void Postfix(List<RelicModel>? ____currentRelics, IPlayerCollection ____playerCollection,
        RelicGrabBag ____sharedGrabBag, Rng ____rng)
    {
        // An empty chest (e.g. Silver Crucible) has already ended relic picking.
        if (____currentRelics is not { Count: > 0 } relics) return;
        var runState = ____playerCollection.Players[0].RunState;
        if (!RichLoot.IsActive(runState)) return;
        // Same shared bag and RNG as vanilla, so every co-op peer rolls the identical extra relic.
        var rarity = RelicFactory.RollRarity(____rng);
        relics.Add(____sharedGrabBag.PullFromFront(rarity, runState) ?? RelicFactory.FallbackRelic);
    }
}

[HarmonyPatch(typeof(NTreasureRoomRelicCollection), nameof(NTreasureRoomRelicCollection.InitializeRelics))]
internal static class RichLootHolderPatch
{
    [HarmonyPrefix]
    private static void Prefix(List<NTreasureRoomRelicHolder> ____multiplayerHolders)
    {
        var relics = RunManager.Instance.TreasureRoomRelicSynchronizer.CurrentRelics;
        // The scene only has four co-op holders; a full lobby plus the extra relic needs a fifth.
        if (relics == null || ____multiplayerHolders.Count is < 4 || relics.Count <= ____multiplayerHolders.Count) return;
        var top = ____multiplayerHolders[0];
        var bottom = ____multiplayerHolders[1];
        var template = ____multiplayerHolders[3];
        while (____multiplayerHolders.Count < relics.Count)
        {
            var holder = (NTreasureRoomRelicHolder)template.Duplicate();
            holder.Name = "MultiplayerRelicHolder" + (____multiplayerHolders.Count + 1);
            template.GetParent().AddChild(holder);
            holder.Position = new Vector2(top.Position.X, bottom.Position.Y);
            ____multiplayerHolders.Add(holder);
        }
    }
}
