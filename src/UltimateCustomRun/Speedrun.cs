using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Nodes.TopBar;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace UltimateCustomRun;

public sealed class Speedrun : ModifierModel
{
    internal const string DisplayTitle = "Speedrun";
    internal const string DisplayDescription = "Every minute after [blue]30[/blue] minutes you lose 5 HP.";
    internal const int HpLoss = 5;
    private bool _pending;
    private int _penaltyMinutes;

    [SavedProperty]
    public int SpeedrunPenaltyMinutes
    {
        get => _penaltyMinutes;
        set { AssertMutable(); _penaltyMinutes = Math.Max(0, value); }
    }

    protected override string IconPath => ImageHelper.GetImagePath("packed/modifiers/terminal.png");
    protected override void AfterRunCreated(RunState runState) => _pending = false;
    protected override void AfterRunLoaded(RunState runState) => _pending = false;

    internal static int DueMinutes(long runSeconds, int limitMinutes) =>
        (int)Math.Clamp(runSeconds / 60 - limitMinutes, 0, int.MaxValue);

    internal static bool CanTick(RunManager manager) => manager.IsInProgress && !manager.IsGameOver &&
        !manager.IsCleaningUp && (!manager.IsPaused || !manager.IsSingleplayerOrFakeMultiplayer) && manager.WinTime == 0 &&
        !manager.NetService.IsGameLoading && !manager.ActionExecutor.IsPaused;

    internal void Tick(RunManager manager)
    {
        if (_pending || !CanTick(manager) || manager.NetService.Type == NetGameType.Client) return;
        var due = DueMinutes(manager.RunTime, ModifierValues.Get(this));
        if (due <= SpeedrunPenaltyMinutes) return;
        var owner = RunState.Players.First(player => player.NetId == manager.NetService.NetId);
        _pending = true;
        try { manager.ActionQueueSynchronizer.RequestEnqueue(new SpeedrunPenaltyAction(owner, due)); }
        catch { _pending = false; throw; }
    }

    internal void ClearPending() => _pending = false;

    internal async Task ApplyMinutes(int due, Func<Task> loseHp, Func<bool> ended)
    {
        while (SpeedrunPenaltyMinutes < due && !ended())
        {
            SpeedrunPenaltyMinutes++;
            await loseHp();
        }
    }

    internal async Task Apply(GameAction action, int due)
    {
        var manager = RunManager.Instance;
        try
        {
            // Once queued, every peer must make the same decision. Local pause and
            // loading flags are only scheduling guards, never execution guards.
            if (!ReferenceEquals(manager.DebugOnlyGetState(), RunState) || manager.IsGameOver || manager.IsCleaningUp) return;
            var context = new GameActionPlayerChoiceContext(action);
            // Match native HP-loss cards: bypass Block and powered damage modifiers,
            // while preserving HP-loss hooks, animations, death and revival handling.
            await ApplyMinutes(due, () => CreatureCmd.Damage(context, RunState.Players.Where(player => !player.Creature.IsDead)
                    .Select(player => player.Creature).ToArray(), HpLoss,
                    ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move, null, null, null),
                () => manager.IsGameOver || manager.WinTime > 0);
        }
        finally { _pending = false; }
    }
}

// The host's clock schedules one native queued action for all peers. Clients never
// sample their own clock for penalties, avoiding timing-based co-op desyncs.
[HarmonyPatch(typeof(NRunTimer), "OnTimerTimeout")]
internal static class SpeedrunTimerPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        var manager = RunManager.Instance;
        if (!manager.IsInProgress) return;
        manager.DebugOnlyGetState()?.Modifiers.OfType<Speedrun>().FirstOrDefault()?.Tick(manager);
    }
}

internal sealed class SpeedrunPenaltyAction(Player player, int due) : GameAction
{
    public override ulong OwnerId => player.NetId;
    public override GameActionType ActionType => GameActionType.Any;
    protected override Task ExecuteAction() => player.RunState.Modifiers.OfType<Speedrun>().FirstOrDefault()
        is { } modifier ? modifier.Apply(this, due) : Task.CompletedTask;
    protected override void CancelAction() => player.RunState.Modifiers.OfType<Speedrun>().FirstOrDefault()?.ClearPending();
    public override INetAction ToNetAction() => new NetSpeedrunPenaltyAction { Due = due };
}

public struct NetSpeedrunPenaltyAction : INetAction
{
    public int Due;
    public GameAction ToGameAction(Player player) => new SpeedrunPenaltyAction(player, Due);
    public void Serialize(PacketWriter writer) => writer.WriteInt(Due);
    public void Deserialize(PacketReader reader) => Due = reader.ReadInt();
}
