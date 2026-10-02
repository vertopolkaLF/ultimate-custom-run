using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.sts2.Core.Nodes.TopBar;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace UltimateCustomRun;

public sealed class MysteryEvents : ModifierModel
{
    internal const string DisplayTitle = "???";
    internal const string DisplayDescription = "Encounter [blue]3[/blue] additional Events after Neow.";
    internal const int EventCount = 3;
    internal int EventLimit => ModifierValues.Get(this);
    internal static string DescriptionText(int count) =>
        $"Encounter [blue]{count}[/blue] additional {(count == 1 ? "Event" : "Events")} after Neow.";
    private int _stage;
    private HashSet<ulong> _ready = [];
    private bool _requested;

    // 0 = Neow, 1..EventLimit = extra event, EventLimit + 1 = main map.
    [SavedProperty]
    public int MysteryStage
    {
        get => _stage;
        set { AssertMutable(); _stage = Math.Clamp(value, 0, 11); }
    }

    protected override string IconPath => ImageHelper.GetImagePath("atlases/ui_atlas.sprites/map/icons/map_unknown.tres");
    protected override void AfterRunCreated(RunState runState) => ResetTransientState();
    protected override void AfterRunLoaded(RunState runState) => ResetTransientState();
    private void ResetTransientState() { _ready = []; _requested = false; }

    internal static MysteryEvents? For(IRunState? state) => state?.Modifiers.OfType<MysteryEvents>().FirstOrDefault();
    internal static bool IsUnconditionalEvent(EventModel eventModel) => eventModel is not AncientEventModel &&
        eventModel.GetType().GetMethod(nameof(EventModel.IsAllowed))?.DeclaringType == typeof(EventModel);

    internal static int SelectEventIndex(IReadOnlyList<EventModel> events, int start, IReadOnlySet<ModelId> visited)
    {
        var repeat = -1;
        for (var offset = 0; offset < events.Count; offset++)
        {
            var index = (start % events.Count + offset) % events.Count;
            var candidate = events[index];
            if (!IsUnconditionalEvent(candidate)) continue;
            if (!visited.Contains(candidate.Id)) return index;
            if (repeat < 0) repeat = index;
        }
        // Repeats are preferable to letting the native exhaustion fallback pick
        // an event with prerequisites. Never broaden this pool after exhaustion.
        if (repeat >= 0) return repeat;
        throw new InvalidOperationException("??? has no unconditional events in the unlocked event pool.");
    }
    internal static bool BeforeMainMap(IRunState state) => state.CurrentActIndex == 0 &&
        state.CurrentMapCoord == state.Map.StartingMapPoint.coord;
    internal bool IsExtraRoom(IRunState state) => MysteryStage > 0 && BeforeMainMap(state);
    internal bool NeedsEvents(IRunState state) => MysteryStage <= EventLimit && BeforeMainMap(state);
    public override bool ShouldProceedToNextMapPoint() => !NeedsEvents(RunState);

    internal bool RecordReady(ulong playerId, int stage, IEnumerable<ulong> playerIds)
    {
        if (stage != MysteryStage || stage > EventLimit || !_ready.Add(playerId)) return false;
        return playerIds.All(id => _ready.Contains(id));
    }

    internal void RequestProceed(Player player)
    {
        if (_requested) return;
        _requested = true;
        try { RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new MysteryProceedAction(player, MysteryStage)); }
        catch { _requested = false; throw; }
    }

    internal async Task Proceed(Player player, int stage)
    {
        var manager = RunManager.Instance;
        if (!ReferenceEquals(manager.DebugOnlyGetState(), RunState) || manager.IsGameOver || manager.IsCleaningUp ||
            !NeedsEvents(RunState) || RunState.BaseRoom is not EventRoom ||
            !manager.EventSynchronizer.GetEventForPlayer(player).IsFinished) return;
        if (!RecordReady(player.NetId, stage, RunState.Players.Select(p => p.NetId))) return;

        MysteryStage++;
        ResetTransientState();
        if (MysteryStage <= EventLimit)
        {
            // Keep the actual map coordinate and ActFloor at Neow. The native room
            // transition adds a separate Unknown history entry (and TotalFloor).
            // Its pre-entry save preserves the event pool/RNG for reloads.
            await manager.FadeOut();
            await manager.EnterMapPointInternal(1, MapPointType.Unknown, null, saveGame: true);
            await manager.FadeIn();
        }
        else
        {
            var room = (EventRoom)RunState.BaseRoom;
            room.MarkPreFinished();
            await SaveManager.Instance.SaveRun(room);
            await NEventRoom.Proceed();
        }
    }

    // History includes the extra floors, while map coordinates never change.
    internal static int HistoryIndex(IRunState state, int actIndex, int row) => row +
        (actIndex == 0 && row > 0 && For(state) is { } modifier ? Math.Min(modifier.MysteryStage, modifier.EventLimit) : 0);
}

[HarmonyPatch(typeof(RoomSet), nameof(RoomSet.EnsureNextEventIsValid))]
internal static class MysteryEventPoolPatch
{
    [HarmonyPrefix]
    internal static bool Prefix(RoomSet __instance, RunState __0)
    {
        if (MysteryEvents.For(__0)?.IsExtraRoom(__0) != true) return true;
        var index = MysteryEvents.SelectEventIndex(__instance.events, __instance.eventsVisited, __0.VisitedEventIds);
        var currentIndex = __instance.eventsVisited % __instance.events.Count;
        __instance.eventsVisited += (index - currentIndex + __instance.events.Count) % __instance.events.Count;
        return false;
    }
}

internal sealed class MysteryProceedAction(Player player, int stage) : GameAction
{
    public override ulong OwnerId => player.NetId;
    public override GameActionType ActionType => GameActionType.NonCombat;
    protected override Task ExecuteAction() => MysteryEvents.For(player.RunState)?.Proceed(player, stage) ?? Task.CompletedTask;
    public override INetAction ToNetAction() => new NetMysteryProceedAction { Stage = stage };
}

public struct NetMysteryProceedAction : INetAction
{
    public int Stage;
    public GameAction ToGameAction(Player player) => new MysteryProceedAction(player, Stage);
    public void Serialize(PacketWriter writer) => writer.WriteInt(Stage);
    public void Deserialize(PacketReader reader) => Stage = reader.ReadInt();
}

[HarmonyPatch(typeof(NEventRoom), nameof(NEventRoom.Proceed))]
internal static class MysteryProceedPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ref Task __result)
    {
        var manager = RunManager.Instance;
        var state = manager.DebugOnlyGetState();
        if (state == null || MysteryEvents.For(state) is not { } modifier || !modifier.NeedsEvents(state) ||
            state.BaseRoom is not EventRoom room || (modifier.MysteryStage == 0 && room.CanonicalEvent is not Neow)) return true;
        var player = LocalContext.GetMe(state);
        if (player != null && manager.EventSynchronizer.GetEventForPlayer(player).IsFinished) modifier.RequestProceed(player);
        __result = Task.CompletedTask;
        return false;
    }
}

[HarmonyPatch(typeof(RunManager), "RollRoomTypeFor")]
internal static class MysteryRoomTypePatch
{
    [HarmonyPrefix]
    internal static bool Prefix(RunManager __instance, MapPointType __0, ref RoomType __result)
    {
        var state = __instance.DebugOnlyGetState();
        if (__0 != MapPointType.Unknown || state == null || MysteryEvents.For(state) is not { } modifier ||
            !modifier.IsExtraRoom(state)) return true;
        // Bypass UnknownMapPoint odds entirely, including Deadly Events and relics.
        __result = RoomType.Event;
        return false;
    }
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.LoadIntoLatestMapCoord))]
internal static class MysteryLoadPatch
{
    [HarmonyPrefix]
    private static bool Prefix(RunManager __instance, AbstractRoom? __0, ref Task __result)
    {
        var state = __instance.DebugOnlyGetState();
        if (state == null || MysteryEvents.For(state) is not { } modifier || !modifier.IsExtraRoom(state)) return true;
        __result = __instance.EnterMapPointInternal(1, MapPointType.Unknown, __0, saveGame: false);
        return false;
    }
}

[HarmonyPatch(typeof(NTopBarRoomIcon), "GetCurrentMapPointType")]
internal static class MysteryRoomIconPatch
{
    [HarmonyPostfix]
    private static void Postfix(IRunState ____runState, ref MapPointType __result)
    {
        if (MysteryEvents.For(____runState)?.IsExtraRoom(____runState) == true) __result = MapPointType.Unknown;
    }
}

[HarmonyPatch(typeof(RunState), nameof(RunState.GetHistoryEntryFor))]
internal static class MysteryHistoryPatch
{
    [HarmonyPrefix]
    private static bool Prefix(RunState __instance, MapLocation __0, ref MapPointHistoryEntry? __result)
    {
        if (__0.actIndex != 0 || !__0.coord.HasValue || __0.coord.Value.row <= 0 || MysteryEvents.For(__instance) == null) return true;
        var index = MysteryEvents.HistoryIndex(__instance, __0.actIndex, __0.coord.Value.row);
        __result = __instance.MapPointHistory.Count > __0.actIndex && index < __instance.MapPointHistory[__0.actIndex].Count
            ? __instance.MapPointHistory[__0.actIndex][index] : null;
        return false;
    }
}

[HarmonyPatch(typeof(NNormalMapPoint), "UpdateIcon")]
internal static class MysteryMapIconHistoryPatch
{
    internal static int Index(int row, NNormalMapPoint point)
    {
        var state = (IRunState)AccessTools.Field(typeof(NMapPoint), "_runState").GetValue(point)!;
        return MysteryEvents.HistoryIndex(state, state.CurrentActIndex, row);
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var changed = 0;
        foreach (var instruction in instructions)
        {
            yield return instruction;
            if (!instruction.LoadsField(AccessTools.Field(typeof(MapCoord), nameof(MapCoord.row)))) continue;
            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MysteryMapIconHistoryPatch), nameof(Index)));
            changed++;
        }
        if (changed != 2) throw new InvalidOperationException("Map history icon indexing changed; cannot safely account for extra event floors.");
    }
}

[HarmonyPatch(typeof(NMapPointHistoryHoverTip), nameof(NMapPointHistoryHoverTip.Create))]
internal static class MysteryHistoryFloorPatch
{
    [HarmonyPrefix]
    private static void Prefix(ref int __0, MapPointHistoryEntry __2)
    {
        var state = RunManager.Instance.DebugOnlyGetState();
        if (state == null || MysteryEvents.For(state) == null) return;
        var floor = 0;
        foreach (var entry in state.MapPointHistory.SelectMany(act => act))
        {
            floor++;
            if (ReferenceEquals(entry, __2)) { __0 = floor; return; }
        }
    }
}
