using System.Reflection.Emit;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace UltimateCustomRun;

[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.SetMap))]
internal static class BossChainMapUiPatch
{
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
            var node = NBossMapPoint.Create(point, screen, run);
            nodes.Add(point.coord, node);
            points.AddChildSafely(node);
        }
        // Fit the entire chain in the native boss area above the ordinary map.
        var lastNormalRow = run.Map.BossMapPoint.coord.row - 1;
        var distY = (float)AccessTools.Field(typeof(NMapScreen), "_distY").GetValue(screen)!;
        var firstY = 740f - (lastNormalRow + 1) * distY;
        var finalY = -2052f;
        for (var i = 0; i < chain.Count; i++)
        {
            var node = nodes[chain[i].coord];
            var y = firstY + (finalY - firstY) * i / (chain.Count - 1);
            node.Position = new Vector2(chain[i].PointType == MapPointType.Boss ? -200f : -80f, y);
            if (node is NBossMapPoint) node.Scale = new Vector2(0.6f, 0.6f);
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

[HarmonyPatch(typeof(NBossMapPoint), nameof(NBossMapPoint._Ready))]
internal static class BossChainVisualPatch
{
    internal static EncounterModel Resolve(EncounterModel original, NBossMapPoint node)
    {
        var run = (IRunState)AccessTools.Field(typeof(NMapPoint), "_runState").GetValue(node)!;
        if (!DoubleTrouble.IsEnabled(run)) return original;
        var bosses = BossChainActMap.Chain(run.Map).Where(point => point.PointType == MapPointType.Boss).ToList();
        var index = bosses.FindIndex(point => point.coord == node.Point.coord);
        return index switch { 1 => run.Act.SecondBossEncounter!, 2 => DoubleTrouble.ThirdEncounter(run.Act), _ => original };
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var getters = new[] { AccessTools.PropertyGetter(typeof(ActModel), nameof(ActModel.BossEncounter)),
            AccessTools.PropertyGetter(typeof(ActModel), nameof(ActModel.SecondBossEncounter)) };
        var changed = 0;
        foreach (var instruction in instructions)
        {
            yield return instruction;
            if (!getters.Any(instruction.Calls)) continue;
            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(BossChainVisualPatch), nameof(Resolve)));
            changed++;
        }
        if (changed != 2) throw new InvalidOperationException("Boss node initialization changed.");
    }
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
            if (!instruction.Calls(getter)) continue;
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
