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
        var row = BossMapPoint.coord.row;
        var col = BossMapPoint.coord.col;
        var extraRows = count - 1 + (campfires ? count - 1 : 0);
        Grid = new MapPoint?[source.GetColumnCount(), row + extraRows];
        foreach (var point in source.GetAllMapPoints()) Grid[point.coord.col, point.coord.row] = point;
        foreach (var child in BossMapPoint.Children.ToArray()) BossMapPoint.RemoveChildPoint(child);
        var previous = BossMapPoint;
        for (var boss = 1; boss < count; boss++)
        {
            if (campfires)
            {
                var rest = new MapPoint(col, ++row) { PointType = MapPointType.RestSite, CanBeModified = false };
                Grid[col, row] = rest;
                previous.AddChildPoint(rest);
                previous = rest;
            }
            var next = new MapPoint(col, ++row) { PointType = MapPointType.Boss, CanBeModified = false };
            previous.AddChildPoint(next);
            if (boss == count - 1) SecondBossMapPoint = next;
            else Grid[col, row] = next;
            previous = next;
        }
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
