using HarmonyLib;
using MegaCrit.Sts2.Core.Map;

namespace UltimateCustomRun;

// Keep native first/final boss fields and serialize intermediate nodes in the grid.
internal sealed class BossChainActMap : ActMap
{
    public override MapPoint BossMapPoint { get; }
    public override MapPoint StartingMapPoint { get; }
    public override MapPoint? SecondBossMapPoint { get; }
    protected override MapPoint?[,] Grid { get; }

    internal BossChainActMap(ActMap source, int count, bool campfires)
    {
        BossMapPoint = source.BossMapPoint;
        StartingMapPoint = source.StartingMapPoint;
        startMapPoints.UnionWith(source.startMapPoints);
        (Grid, SecondBossMapPoint) = Build(source, count, campfires);
    }

    // Blitz progression and its top-bar map require a BlitzActMap, which is sealed, so its chain is rebuilt in place.
    internal static BlitzActMap ExtendInPlace(BlitzActMap map, int count, bool campfires)
    {
        var (grid, secondBoss) = Build(map, count, campfires);
        AccessTools.Field(typeof(BlitzActMap), "<Grid>k__BackingField").SetValue(map, grid);
        AccessTools.Field(typeof(BlitzActMap), "<SecondBossMapPoint>k__BackingField").SetValue(map, secondBoss);
        return map;
    }

    private static (MapPoint?[,] Grid, MapPoint? SecondBoss) Build(ActMap source, int count, bool campfires)
    {
        var boss = source.BossMapPoint;
        var row = boss.coord.row;
        var col = boss.coord.col;
        var extraRows = count - 1 + (campfires ? count - 1 : 0);
        var grid = new MapPoint?[source.GetColumnCount(), row + extraRows];
        MapPoint? secondBoss = null;
        foreach (var point in source.GetAllMapPoints()) grid[point.coord.col, point.coord.row] = point;
        foreach (var child in boss.Children.ToArray()) boss.RemoveChildPoint(child);
        var previous = boss;
        for (var index = 1; index < count; index++)
        {
            if (campfires)
            {
                var rest = new MapPoint(col, ++row) { PointType = MapPointType.RestSite, CanBeModified = false };
                grid[col, row] = rest;
                previous.AddChildPoint(rest);
                previous = rest;
            }
            var next = new MapPoint(col, ++row) { PointType = MapPointType.Boss, CanBeModified = false };
            previous.AddChildPoint(next);
            if (index == count - 1) secondBoss = next;
            else grid[col, row] = next;
            previous = next;
        }
        return (grid, secondBoss);
    }

    internal static IReadOnlyList<MapPoint> Chain(ActMap map)
    {
        var result = new List<MapPoint> { map.BossMapPoint };
        var current = map.BossMapPoint;
        while (current.Children.Count == 1)
        {
            current = current.Children.Single();
            if (current.PointType is not (MapPointType.Boss or MapPointType.RestSite) || result.Contains(current)) break;
            result.Add(current);
        }
        return result;
    }
}
