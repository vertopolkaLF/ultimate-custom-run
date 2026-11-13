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
    internal static void Postfix(List<RelicModel>? ____currentRelics, IRunState ____runState,
        RelicGrabBag ____sharedGrabBag, Rng ____rng)
    {
        // An empty chest (e.g. Silver Crucible) has already ended relic picking.
        if (____currentRelics is not { Count: > 0 } relics) return;
        var runState = ____runState;
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
    // Native holders are 136px wide; the six-relic layout spaces neighbors 224px apart and rows 220px apart.
    private const float Spacing = 224f;
    private const float RowOffset = 110f;

    /// <summary>Center of a holder, relative to the screen center, in a two-row arrangement.</summary>
    internal static Vector2 PositionFor(int index, int count)
    {
        var topCount = (count + 1) / 2;
        var isTop = index < topCount;
        var columns = isTop ? topCount : count - topCount;
        var column = isTop ? index : index - topCount;
        return new Vector2((column - (columns - 1) / 2f) * Spacing, isTop ? -RowOffset : RowOffset);
    }

    [HarmonyPrefix]
    private static void Prefix(List<Control> ____holderLayouts)
    {
        var relics = RunManager.Instance.TreasureRoomRelicSynchronizer.CurrentRelics;
        // The scene has hand-placed layouts for one to six relics, and the game shows layout[count - 1].
        // A full lobby with Rich Loot can offer seven, so add generated layouts for the missing counts.
        if (relics == null || ____holderLayouts.Count == 0) return;
        while (____holderLayouts.Count < relics.Count)
        {
            var template = ____holderLayouts[^1];
            var layout = (Control)template.Duplicate();
            var count = ____holderLayouts.Count + 1;
            layout.Name = "Layout" + count;
            layout.Visible = false;
            var holders = layout.GetChildren().OfType<NTreasureRoomRelicHolder>().ToList();
            if (holders.Count == 0) return;
            while (holders.Count < count)
            {
                var holder = (NTreasureRoomRelicHolder)holders[^1].Duplicate();
                holder.Name = (holders.Count + 1).ToString();
                layout.AddChild(holder);
                holders.Add(holder);
            }
            for (var i = 0; i < holders.Count; i++)
            {
                var holder = holders[i];
                var halfWidth = (holder.OffsetRight - holder.OffsetLeft) / 2f;
                var halfHeight = (holder.OffsetBottom - holder.OffsetTop) / 2f;
                var center = PositionFor(i, holders.Count);
                holder.OffsetLeft = center.X - halfWidth;
                holder.OffsetRight = center.X + halfWidth;
                holder.OffsetTop = center.Y - halfHeight;
                holder.OffsetBottom = center.Y + halfHeight;
            }
            template.GetParent().AddChild(layout);
            ____holderLayouts.Add(layout);
        }
    }
}
