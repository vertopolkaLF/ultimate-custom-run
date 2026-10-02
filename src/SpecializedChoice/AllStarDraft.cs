using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Runs;

namespace SpecializedChoice;

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
        for (var i = 0; i < RewardCount; i++)
        {
            var offers = CardFactory.CreateForReward(player, SpecializedDraftReward.OfferCount,
                Options(ModelDb.CardPool<ColorlessCardPool>())).ToList();
            var selected = await SpecializedDraftReward.SelectReward(player, offers);
            if (selected == null) continue;
            var result = await CardPileCmd.Add(selected, PileType.Deck);
            CardCmd.PreviewCardPileAdd([result]);
            await Cmd.CustomScaledWait(0.6f, 1.2f);
        }
    }
}
