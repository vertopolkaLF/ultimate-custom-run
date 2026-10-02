using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Rooms;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;

namespace UltimateCustomRun;

public sealed class DoubleTrouble : ModifierModel
{
    internal const string DisplayTitle = "Double Trouble";
    internal const string DisplayDescription = "Fight [blue]2[/blue] different Bosses at the end of each Act. Receive rewards from each.";
    protected override string IconPath => ImageHelper.GetImagePath("packed/modifiers/big_game_hunter.png");

    internal static bool IsEnabled(IRunState state) => state.Modifiers.Any(modifier => modifier is DoubleTrouble);

    private sealed class ExtraBoss { internal required EncounterModel Encounter; }
    private static readonly ConditionalWeakTable<ActModel, ExtraBoss> ThirdBosses = new();
    internal static DoubleTrouble? For(IRunState state) => state.Modifiers.OfType<DoubleTrouble>().FirstOrDefault();

    protected override void AfterRunLoaded(RunState runState) => ConfigureThirdBosses(runState.Acts, ModifierValues.Get(this));

    public override ActMap ModifyGeneratedMapLate(IRunState runState, ActMap map, int actIndex) =>
        ModifierValues.Get(this) == 3 || CampfiresBetweenBosses.IsEnabled(runState)
            ? new BossChainActMap(map, ModifierValues.Get(this), CampfiresBetweenBosses.IsEnabled(runState)) : map;

    internal static EncounterModel ThirdEncounter(ActModel act) => act.AllBossEncounters
        .Where(boss => boss.Id != act.BossEncounter.Id && boss.Id != act.SecondBossEncounter?.Id)
        .OrderBy(boss => boss.Id.ToString(), StringComparer.Ordinal).FirstOrDefault()
        ?? throw new InvalidOperationException($"No distinct third boss is available for {act.Id}.");

    internal static void ConfigureThirdBosses(IEnumerable<ActModel> acts, int count)
    {
        foreach (var act in acts)
        {
            ThirdBosses.Remove(act);
            if (count == 3) ThirdBosses.Add(act, new ExtraBoss { Encounter = ThirdEncounter(act) });
        }
    }

    internal static EncounterModel? ExtraEncounter(ActModel act) =>
        ThirdBosses.TryGetValue(act, out var extra) ? extra.Encounter : null;

    internal static void AddSecondBosses(IEnumerable<ActModel> acts, Rng rng, int count = 2)
    {
        var actList = acts.ToList();
        foreach (var act in actList)
        {
            // Preserve A10's already generated second boss, including its RNG roll.
            if (act.HasSecondBoss) continue;
            var candidates = act.AllBossEncounters.Where(boss => boss.Id != act.BossEncounter.Id).ToList();
            if (candidates.Count == 0) throw new InvalidOperationException($"No distinct second boss is available for {act.Id}.");
            act.SetSecondBossEncounter(rng.NextItem(candidates));
        }
        ConfigureThirdBosses(actList, count);
    }

    // Vanilla suppresses rewards for every final-act boss. This modifier grants
    // the normal boss reward set for each, without changing act progression.
    internal static int RewardActIndex(IRunState state) => IsEnabled(state) ? -1 : state.CurrentActIndex;
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.GenerateRooms))]
internal static class DoubleTroubleRoomsPatch
{
    [HarmonyPostfix]
    private static void Postfix(RunManager __instance)
    {
        var state = __instance.DebugOnlyGetState();
        if (state != null && DoubleTrouble.IsEnabled(state)) DoubleTrouble.AddSecondBosses(state.Acts, state.Rng.UpFront, ModifierValues.Get(DoubleTrouble.For(state)!));
    }
}

[HarmonyPatch(typeof(RewardsSet), nameof(RewardsSet.WithRewardsFromRoom))]
internal static class DoubleTroubleRewardsPatch
{
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var getter = AccessTools.PropertyGetter(typeof(IRunState), nameof(IRunState.CurrentActIndex));
        var changed = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(getter))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(DoubleTrouble), nameof(DoubleTrouble.RewardActIndex));
                changed++;
            }
            yield return instruction;
        }
        if (changed != 1) throw new InvalidOperationException("Final-act boss reward guard changed; cannot safely grant Double Trouble rewards.");
    }
}

[HarmonyPatch(typeof(ActModel), nameof(ActModel.PullNextEncounter))]
internal static class DoubleTroubleEncounterPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ActModel __instance, RoomType __0, ref EncounterModel __result)
    {
        if (__0 != RoomType.Boss || DoubleTrouble.ExtraEncounter(__instance) is not { } boss) return true;
        var rooms = (RoomSet)AccessTools.Field(typeof(ActModel), "_rooms").GetValue(__instance)!;
        if (rooms.bossEncountersVisited < 2) return true;
        __result = boss;
        return false;
    }
}

[HarmonyPatch(typeof(ActModel), nameof(ActModel.AssetPaths), MethodType.Getter)]
internal static class DoubleTroubleAssetsPatch
{
    [HarmonyPostfix]
    private static void Postfix(ActModel __instance, ref IEnumerable<string> __result)
    {
        if (DoubleTrouble.ExtraEncounter(__instance) is { } boss)
            __result = __result.Concat(boss.MapNodeAssetPaths).Distinct();
    }
}
