using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;

namespace UltimateCustomRun;

public sealed class CardSwarm : ModifierModel
{
    internal const string DisplayTitle = "Card Swarm";
    internal const string DisplayDescription = "Card rewards contain [blue]1[/blue] extra card.";
    internal const int ExtraCards = 1;
    protected override string IconPath => ImageHelper.GetImagePath("packed/modifiers/draft.png");

    internal static bool IsActive(IRunState runState) =>
        runState.Modifiers.Any(modifier => modifier is CardSwarm);

    internal static int OfferCount(Player player, int baseCount) =>
        IsActive(player.RunState) ? baseCount + ExtraCards : baseCount;
}

// The saved OptionCount stays vanilla; extending only at generation time keeps reloads from stacking extra cards.
[HarmonyPatch(typeof(CardReward), nameof(CardReward.Populate))]
internal static class CardSwarmRewardPatch
{
    [ThreadStatic] internal static bool Generating;

    [HarmonyPrefix]
    private static void Prefix() => Generating = true;

    [HarmonyFinalizer]
    private static void Finalizer() => Generating = false;
}

[HarmonyPatch(typeof(CardFactory), nameof(CardFactory.CreateForReward),
    [typeof(Player), typeof(int), typeof(CardCreationOptions)])]
internal static class CardSwarmFactoryPatch
{
    [HarmonyPrefix]
    private static void Prefix(Player player, ref int cardCount)
    {
        if (!CardSwarmRewardPatch.Generating) return;
        CardSwarmRewardPatch.Generating = false;
        cardCount = CardSwarm.OfferCount(player, cardCount);
    }
}
