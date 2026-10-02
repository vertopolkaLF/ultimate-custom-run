using System.Reflection.Emit;
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
    internal const string DisplayDescription = "Fight [blue]2[/blue] different Bosses at the end of each Act. Receive rewards from both.";
    protected override string IconPath => ImageHelper.GetImagePath("packed/modifiers/big_game_hunter.png");

    internal static bool IsEnabled(IRunState state) => state.Modifiers.Any(modifier => modifier is DoubleTrouble);

    internal static void AddSecondBosses(IEnumerable<ActModel> acts, Rng rng)
    {
        foreach (var act in acts)
        {
            // Preserve A10's already generated second boss, including its RNG roll.
            if (act.HasSecondBoss) continue;
            var candidates = act.AllBossEncounters.Where(boss => boss.Id != act.BossEncounter.Id).ToList();
            if (candidates.Count == 0) throw new InvalidOperationException($"No distinct second boss is available for {act.Id}.");
            act.SetSecondBossEncounter(rng.NextItem(candidates));
        }
    }

    // Vanilla suppresses rewards for every final-act boss. This modifier grants
    // the normal boss reward set for both, without changing act progression.
    internal static int RewardActIndex(IRunState state) => IsEnabled(state) ? -1 : state.CurrentActIndex;
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.GenerateRooms))]
internal static class DoubleTroubleRoomsPatch
{
    [HarmonyPostfix]
    private static void Postfix(RunManager __instance)
    {
        var state = __instance.DebugOnlyGetState();
        if (state != null && DoubleTrouble.IsEnabled(state)) DoubleTrouble.AddSecondBosses(state.Acts, state.Rng.UpFront);
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
