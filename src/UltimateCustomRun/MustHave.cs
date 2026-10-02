using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Rewards;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Rewards;

namespace UltimateCustomRun;

public sealed class MustHave : ModifierModel
{
    internal const string DisplayTitle = "Must Have";
    internal const string DisplayDescription = "Take a card from every card reward before leaving. Rerolls remain available; non-card rewards remain optional. Passing during Super Draft is allowed.";
    private static readonly ConditionalWeakTable<CardReward, object> PassingAllowed = new();
    private static readonly ConditionalWeakTable<Player, List<RewardsSet>> PendingSets = new();
    protected override string IconPath => ImageHelper.GetImagePath("packed/modifiers/cursed_run.png");

    internal static bool IsActive(Player player) => player.RunState.Modifiers.Any(modifier => modifier is MustHave);
    internal static void AllowPassing(CardReward reward) => PassingAllowed.GetValue(reward, _ => new object());
    internal static bool IsSuperDraftReward(CardReward reward) => PassingAllowed.TryGetValue(reward, out _);
    internal static bool RequiresCard(CardReward reward) => IsActive(reward.Player) && !IsSuperDraftReward(reward);
    internal static bool IsPending(Reward reward) => !reward.SuccessfullySelected && (reward switch
    {
        CardReward card => RequiresCard(card),
        LinkedRewardSet linked => linked.Rewards.Any(IsPending),
        _ => false
    });
    internal static bool BlocksLeaving(RewardsSet set) => set.Rewards.Any(IsPending);
    internal static void Track(RewardsSet set) => PendingSets.GetOrCreateValue(set.Player).Add(set);
    internal static void Untrack(RewardsSet set)
    {
        if (PendingSets.TryGetValue(set.Player, out var sets)) sets.Remove(set);
    }

    public override bool ShouldProceedToNextMapPoint() =>
        !RunState.Players.Where(LocalContext.IsMe).Any(player =>
            PendingSets.TryGetValue(player, out var sets) && sets.Any(BlocksLeaving));

    internal static void FilterAlternatives(List<CardRewardAlternative> alternatives) =>
        alternatives.RemoveAll(option => option.AfterSelected != PostAlternateCardRewardAction.DoNothing);
}

[HarmonyPatch(typeof(CardReward), nameof(CardReward.CanSkip), MethodType.Getter)]
internal static class MustHaveSkipPatch
{
    [HarmonyPostfix]
    private static void Postfix(CardReward __instance, ref bool __result)
    {
        if (MustHave.RequiresCard(__instance)) __result = false;
    }
}

// Apply after all relic hooks so healing/sacrifice cannot replace the required card. Rerolls survive.
[HarmonyPatch(typeof(CardRewardAlternative), nameof(CardRewardAlternative.Generate))]
internal static class MustHaveAlternativesPatch
{
    [HarmonyPostfix]
    private static void Postfix(CardReward cardReward, ref IReadOnlyList<CardRewardAlternative> __result)
    {
        if (MustHave.IsSuperDraftReward(cardReward))
        {
            __result = __result.Where(option => option.AfterSelected is PostAlternateCardRewardAction.DoNothing
                or PostAlternateCardRewardAction.EndSelectionAndDoNotCompleteReward).ToList();
            return;
        }
        if (!MustHave.RequiresCard(cardReward)) return;
        var alternatives = __result.ToList();
        MustHave.FilterAlternatives(alternatives);
        __result = alternatives;
    }
}

[HarmonyPatch(typeof(RewardsSetSynchronizer), nameof(RewardsSetSynchronizer.BeginRewardsSet))]
internal static class MustHaveBeginPatch
{
    [HarmonyPrefix]
    private static void Prefix(RewardsSet set) => MustHave.Track(set);
}

[HarmonyPatch(typeof(RewardsSetSynchronizer), "CompleteRewardsSet")]
internal static class MustHaveCompletePatch
{
    [HarmonyPostfix]
    private static void Postfix(object __0) =>
        MustHave.Untrack((RewardsSet)AccessTools.Field(__0.GetType(), "set").GetValue(__0)!);
}

[HarmonyPatch(typeof(NRewardsScreen), "TryEnableProceedButton")]
internal static class MustHaveProceedStatePatch
{
    [HarmonyPrefix]
    private static bool Prefix(RewardsSet ____rewardsSet, NProceedButton ____proceedButton)
    {
        if (!MustHave.BlocksLeaving(____rewardsSet)) return true;
        ____proceedButton.Disable();
        return false;
    }
}

[HarmonyPatch(typeof(NRewardsScreen), "OnProceedButtonPressed")]
internal static class MustHaveProceedPatch
{
    [HarmonyPrefix]
    private static bool Prefix(RewardsSet ____rewardsSet) => !MustHave.BlocksLeaving(____rewardsSet);
}
