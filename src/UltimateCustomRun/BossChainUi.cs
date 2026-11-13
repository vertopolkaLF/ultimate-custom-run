using System.Reflection.Emit;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace UltimateCustomRun;

[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.SetMap))]
internal static class BossChainMapUiPatch
{
    internal static Vector2 PositionForAnchor(Vector2 offset, Vector2 parentSize, Vector2 anchor) =>
        offset + parentSize * anchor;

    internal static float NextCenterY(float previousCenter, float previousHalfHeight, float halfHeight, float gap) =>
        previousCenter - previousHalfHeight - halfHeight - gap;

    // Native map rows stack upward from a fixed baseline: each row takes its tallest icon's height,
    // separated by a uniform gap that fills the map's fixed total height.
    internal static float NativeRowGap(IReadOnlyList<float> rowHeights, bool hasSecondBoss) =>
        (2325f * (hasSecondBoss ? 0.9f : 1f) - rowHeights.Sum()) / (rowHeights.Count - 1);

    internal static float RowCenter(float bottom, IReadOnlyList<float> rowHeights, float gap, int row) =>
        bottom - gap * row - rowHeights.Take(row).Sum() - rowHeights[row] * 0.5f;

    /// <summary>
    /// Fits the ordinary rows plus the boss chain between the native bottom row and the native final boss top.
    /// Returns the gap between ordinary rows, the gap around chain nodes, and each chain node's center.
    /// </summary>
    internal static (float NormalGap, float ChainGap, float[] Centers) FitChain(float bottom,
        IReadOnlyList<float> normalHeights, IReadOnlyList<float> chainHeights, float finalTop)
    {
        var lastNormalRow = normalHeights.Count - 1;
        var free = bottom - finalTop - normalHeights.Sum() - chainHeights.Sum();
        var gap = free / (lastNormalRow + chainHeights.Count);
        var normalGap = gap;
        var chainGap = gap;
        if (gap < 0f)
        {
            // Not enough room: keep chain icons touching and compress the ordinary rows instead.
            chainGap = 0f;
            normalGap = lastNormalRow > 0 ? free / lastNormalRow : 0f;
        }
        var centers = new float[chainHeights.Count];
        var previousCenter = RowCenter(bottom, normalHeights, normalGap, lastNormalRow);
        var previousHalfHeight = normalHeights[lastNormalRow] * 0.5f;
        for (var i = 0; i < centers.Length; i++)
        {
            var halfHeight = chainHeights[i] * 0.5f;
            centers[i] = NextCenterY(previousCenter, previousHalfHeight, halfHeight, chainGap);
            previousCenter = centers[i];
            previousHalfHeight = halfHeight;
        }
        return (normalGap, chainGap, centers);
    }

    internal static IEnumerable<MapPoint> NormalPoints(ActMap map) =>
        map.GetAllMapPoints().Where(point => point.PointType != MapPointType.Boss);

    internal static void PrepareNodes(NMapScreen screen)
    {
        var run = (IRunState)AccessTools.Field(typeof(NMapScreen), "_runState").GetValue(screen)!;
        if (!DoubleTrouble.IsEnabled(run)) return;
        var chain = BossChainActMap.Chain(run.Map);
        if (chain.Count <= 2) return;
        var nodes = (Dictionary<MapCoord, NMapPoint>)AccessTools.Field(typeof(NMapScreen), "_mapPointDictionary").GetValue(screen)!;
        var points = (Control)AccessTools.Field(typeof(NMapScreen), "_points").GetValue(screen)!;
        foreach (var point in chain.Where(point => point.PointType == MapPointType.Boss && !nodes.ContainsKey(point.coord)))
        {
            var node = NBossMapPoint.Create(point, screen, run.Act.BossEncounter, run);
            nodes.Add(point.coord, node);
            points.AddChildSafely(node);
        }
        var map = run.Map;
        var lastNormalRow = map.BossMapPoint.coord.row - 1;
        // Rebuild the native row metrics from the nodes SetMap created in its first loop.
        var created = NormalPoints(map).ToList();
        var rowHeights = new float[map.GetRowCount()];
        foreach (var point in created)
        {
            var node = nodes[point.coord];
            rowHeights[point.coord.row] = Math.Max(rowHeights[point.coord.row], node.Size.Y * node.Scale.Y);
        }
        var nativeGap = NativeRowGap(rowHeights, map.SecondBossMapPoint != null);
        var bottom = 1280f + (points.Size.Y - NGame.devResolution.Y) * 0.5f;
        var finalNode = nodes[chain[^1].coord];
        // Preserve the native final boss's visible top relative to the parchment.
        var finalTop = finalNode.Position.Y + finalNode.PivotOffset.Y - finalNode.Size.Y * finalNode.Scale.Y * 0.5f;
        foreach (var point in chain.Where(point => point.PointType == MapPointType.Boss))
            nodes[point.coord].Scale = new Vector2(0.6f, 0.6f);
        var chainHeights = chain.Select(point => nodes[point.coord].Size.Y * nodes[point.coord].Scale.Y).ToArray();
        var (normalGap, _, centers) = FitChain(bottom, rowHeights.Take(lastNormalRow + 1).ToArray(), chainHeights, finalTop);
        foreach (var point in created.Where(point => point.coord.row <= lastNormalRow).Append(map.StartingMapPoint))
            nodes[point.coord].Position += new Vector2(0f, point.coord.row * (nativeGap - normalGap));
        for (var i = 0; i < chain.Count; i++)
        {
            var node = nodes[chain[i].coord];
            var parentSize = node.GetParent() is Control parent ? parent.Size : Vector2.Zero;
            // Native boss and campfire offsets are relative to the centered anchors.
            var x = PositionForAnchor(new Vector2(chain[i].PointType == MapPointType.Boss ? -200f : -80f, 0f),
                parentSize, new Vector2(node.AnchorLeft, node.AnchorTop)).X;
            node.Position = new Vector2(x, centers[i] - node.PivotOffset.Y);
        }
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var getter = AccessTools.Method(typeof(ActMap), nameof(ActMap.GetAllMapPoints));
        var calls = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(getter))
            {
                calls++;
                if (calls == 1)
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(BossChainMapUiPatch), nameof(NormalPoints));
                }
                else if (calls == 2)
                {
                    // The map argument is already on the stack; Prepare consumes only screen.
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(BossChainMapUiPatch), nameof(PrepareNodes)));
                }
            }
            yield return instruction;
        }
        if (calls != 2) throw new InvalidOperationException("Map rendering changed; cannot safely render the boss chain.");
    }

    [HarmonyPostfix]
    private static void Postfix(NMapScreen __instance)
    {
        var run = (IRunState)AccessTools.Field(typeof(NMapScreen), "_runState").GetValue(__instance)!;
        if (!DoubleTrouble.IsEnabled(run)) return;
        var chain = BossChainActMap.Chain(run.Map);
        var nodes = (Dictionary<MapCoord, NMapPoint>)AccessTools.Field(typeof(NMapScreen), "_mapPointDictionary").GetValue(__instance)!;
        for (var i = 0; i < chain.Count; i++)
        {
            var node = nodes[chain[i].coord];
            if (i > 0) node.FocusNeighborBottom = node.GetPathTo(nodes[chain[i - 1].coord]);
            if (i + 1 < chain.Count) node.FocusNeighborTop = node.GetPathTo(nodes[chain[i + 1].coord]);
        }
    }
}

[HarmonyPatch(typeof(NBossMapPoint), nameof(NBossMapPoint.Create))]
internal static class BossChainVisualPatch
{
    internal static EncounterModel Resolve(EncounterModel original, MapPoint point, IRunState run)
    {
        if (!DoubleTrouble.IsEnabled(run)) return original;
        var bosses = BossChainActMap.Chain(run.Map).Where(boss => boss.PointType == MapPointType.Boss).ToList();
        var index = bosses.FindIndex(boss => boss.coord == point.coord);
        return index switch { 1 => run.Act.SecondBossEncounter!, 2 => DoubleTrouble.ThirdEncounter(run.Act), _ => original };
    }

    // The game now passes each boss node's encounter into Create instead of reading the act in _Ready.
    private static void Prefix(MapPoint point, ref EncounterModel bossEncounter, IRunState runState) =>
        bossEncounter = Resolve(bossEncounter, point, runState);
}

[HarmonyPatch(typeof(NRewardsScreen), "OnProceedButtonPressed")]
internal static class BossChainRewardsProceedPatch
{
    internal static MapPoint ContinuationPoint(MapPoint original, IRunState run) =>
        DoubleTrouble.IsEnabled(run) && run.CurrentMapCoord is { } coord && run.Map.GetPoint(coord) is { } point &&
        point.PointType == MapPointType.Boss && point.Children.Count > 0 ? point : original;

    private static MapPoint Resolve(MapPoint original, NRewardsScreen screen) =>
        ContinuationPoint(original, (IRunState)AccessTools.Field(typeof(NRewardsScreen), "_runState").GetValue(screen)!);

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var getter = AccessTools.PropertyGetter(typeof(ActMap), nameof(ActMap.BossMapPoint));
        var changed = 0;
        foreach (var instruction in instructions)
        {
            yield return instruction;
            // Only the first read decides whether a cleared boss continues to the next boss;
            // the later read is the final-boss fallback for maps without a second boss.
            if (changed > 0 || !instruction.Calls(getter)) continue;
            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(BossChainRewardsProceedPatch), nameof(Resolve)));
            changed++;
        }
        if (changed != 1) throw new InvalidOperationException("Boss reward continuation changed.");
    }
}

[HarmonyPatch(typeof(MapTravel), nameof(MapTravel.GetTravelablePointsFrom))]
internal static class BossChainTravelPatch
{
    [HarmonyPrefix]
    private static bool Prefix(IRunState __0, MapPoint __1, ref IEnumerable<MapPoint> __result)
    {
        if (!DoubleTrouble.IsEnabled(__0) || !BossChainActMap.Chain(__0.Map).Contains(__1)) return true;
        __result = __1.Children;
        return false;
    }
}

[HarmonyPatch(typeof(NMapScreen), "RecalculateTravelability")]
internal static class BossChainMapTravelabilityPatch
{
    internal static MapPoint? LastVisitedChainPoint(RunState run)
    {
        if (!DoubleTrouble.IsEnabled(run) || run.VisitedMapCoords.Count == 0) return null;
        var chain = BossChainActMap.Chain(run.Map);
        if (chain.Count <= 2) return null;
        var lastCoord = run.VisitedMapCoords[^1];
        return chain.FirstOrDefault(point => point.coord == lastCoord);
    }

    [HarmonyPrefix]
    private static bool Prefix(NMapScreen __instance)
    {
        var run = (RunState)AccessTools.Field(typeof(NMapScreen), "_runState").GetValue(__instance)!;
        if (LastVisitedChainPoint(run) is not { } lastPoint) return true;
        var nodes = (Dictionary<MapCoord, NMapPoint>)AccessTools.Field(typeof(NMapScreen), "_mapPointDictionary")
            .GetValue(__instance)!;
        // Vanilla shortcuts from the first boss to the second and from the last
        // grid row back to the first boss. Expanded chains must follow their edges.
        foreach (var node in nodes.Values) node.State = MapPointState.Untravelable;
        foreach (var coord in run.VisitedMapCoords)
            if (nodes.TryGetValue(coord, out var node)) node.State = MapPointState.Traveled;
        foreach (var child in lastPoint.Children) nodes[child.coord].State = MapPointState.Travelable;
        return false;
    }
}
