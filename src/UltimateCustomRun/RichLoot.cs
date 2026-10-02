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
    internal const string DisplayDescription = "Treasure chests contain [blue]1[/blue] extra relic to choose from.";
    internal static string DescriptionText(int count) =>
        $"Treasure chests contain [blue]{count}[/blue] extra {(count == 1 ? "relic" : "relics")} to choose from.";
    protected override string IconPath => ImageHelper.GetImagePath("packed/modifiers/midas.png");

    internal static bool IsActive(IRunState runState) =>
        runState.Modifiers.Any(modifier => modifier is RichLoot);
}

[HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.BeginRelicPicking))]
internal static class RichLootChestPatch
{
    [HarmonyPostfix]
    internal static void Postfix(List<RelicModel>? ____currentRelics, IPlayerCollection ____playerCollection,
        RelicGrabBag ____sharedGrabBag, Rng ____rng)
    {
        // An empty chest (e.g. Silver Crucible) has already ended relic picking.
        if (____currentRelics is not { Count: > 0 } relics) return;
        var runState = ____playerCollection.Players[0].RunState;
        if (runState.Modifiers.OfType<RichLoot>().FirstOrDefault() is not { } modifier) return;
        // Same shared bag and RNG as vanilla, so every co-op peer rolls identical extra relics.
        for (var i = 0; i < ModifierValues.Get(modifier); i++)
        {
            var rarity = RelicFactory.RollRarity(____rng);
            relics.Add(____sharedGrabBag.PullFromFront(rarity, runState) ?? RelicFactory.FallbackRelic);
        }
    }
}

[HarmonyPatch(typeof(NTreasureRoomRelicCollection), nameof(NTreasureRoomRelicCollection.InitializeRelics))]
internal static class RichLootHolderPatch
{
    internal static Vector2 PositionFor(int index, int count, Vector2 top, Vector2 bottom, Vector2 left)
    {
        var topCount = (count + 1) / 2;
        var isTop = index < topCount;
        var columns = isTop ? topCount : count - topCount;
        var column = isTop ? index : index - topCount;
        var spacing = Math.Abs(top.X - left.X);
        return new Vector2((top.X + bottom.X) / 2 + (column - (columns - 1) / 2f) * spacing,
            isTop ? top.Y : bottom.Y);
    }

    [HarmonyPrefix]
    private static void Prefix(List<NTreasureRoomRelicHolder> ____multiplayerHolders)
    {
        var relics = RunManager.Instance.TreasureRoomRelicSynchronizer.CurrentRelics;
        // The scene only has four holders; a full lobby can now offer up to seven relics.
        if (relics == null || ____multiplayerHolders.Count is < 4 || relics.Count <= ____multiplayerHolders.Count) return;
        var top = ____multiplayerHolders[0].Position;
        var bottom = ____multiplayerHolders[1].Position;
        var left = ____multiplayerHolders[2].Position;
        var template = ____multiplayerHolders[3];
        while (____multiplayerHolders.Count < relics.Count)
        {
            var holder = (NTreasureRoomRelicHolder)template.Duplicate();
            holder.Name = "MultiplayerRelicHolder" + (____multiplayerHolders.Count + 1);
            template.GetParent().AddChild(holder);
            ____multiplayerHolders.Add(holder);
        }
        for (var i = 0; i < relics.Count; i++)
            ____multiplayerHolders[i].Position = PositionFor(i, relics.Count, top, bottom, left);
    }
}
