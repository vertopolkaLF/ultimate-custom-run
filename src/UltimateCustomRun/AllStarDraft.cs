using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Runs;

namespace UltimateCustomRun;

public sealed class AllStarDraft : ModifierModel
{
    internal const string DisplayTitle = "All Star - Draft";
    internal const string DisplayDescription = "Start with [blue]5[/blue] [gold]Colorless[/gold] card rewards.";
    internal const int RewardCount = 5;
    protected override string IconPath => ImageHelper.GetImagePath("packed/modifiers/all_star.png");

    public override Func<Task>? GenerateNeowOption(EventModel eventModel) =>
        eventModel.Owner is { } player ? () => ChooseRewards(player) : null;

    internal static CardCreationOptions Options(CardPoolModel colorlessPool) =>
        CardCreationOptions.ForNonCombatWithUniformOdds([colorlessPool])
            .WithFlags(CardCreationFlags.NoRarityModification | CardCreationFlags.NoCardPoolModifications);

    private static async Task ChooseRewards(Player player)
    {
        for (var i = 0; i < ModifierValues.ForPlayer<AllStarDraft>(player); i++)
        {
            var offers = CardFactory.CreateForReward(player, SpecializedDraftReward.OfferCountFor(player),
                Options(ModelDb.CardPool<ColorlessCardPool>())).ToList();
            var selected = await SpecializedDraftReward.SelectReward(player, offers);
            if (selected == null) continue;
            await CardPileCmd.Add(selected, PileType.Deck);
        }
    }
}
