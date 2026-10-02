using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;

namespace UltimateCustomRun;

public sealed class SuperDraft : ModifierModel
{
    internal const string DisplayTitle = "Super Draft";
    internal const string DisplayDescription = "Choose card rewards to add to your starting deck until you skip. Each card chosen has a [blue]0.5%[/blue] chance to add a random [red]Curse[/red] to your deck, doubling with each reward. Chances above [blue]100%[/blue] add guaranteed [red]Curses[/red] plus a chance for another.";
    protected override string IconPath => ImageHelper.GetImagePath("packed/modifiers/draft.png");
    public override Func<Task>? GenerateNeowOption(EventModel eventModel) =>
        eventModel.Owner is { } player ? () => OfferRewards(player) : null;

    internal static double CurseRisk(int offerIndex) => Math.ScaleB(0.005, offerIndex);

    internal static int CurseCount(double risk, double roll) =>
        checked((int)Math.Floor(risk)) + (roll < risk - Math.Floor(risk) ? 1 : 0);

    private static async Task OfferRewards(Player player)
    {
        var options = new CardCreationOptions([player.Character.CardPool], CardCreationSource.Other,
            CardRarityOddsType.RegularEncounter).WithFlags(CardCreationFlags.NoUpgradeRoll);
        for (var offerIndex = 0; ; offerIndex++)
        {
            var reward = new CardReward(options, SpecializedDraftReward.OfferCount, player);
            MustHave.AllowPassing(reward);
            reward.Populate();
            if (!await reward.SelectUnsynchronized()) break;

            var risk = CurseRisk(offerIndex);
            var count = CurseCount(risk, player.PlayerRng.Rewards.NextDouble());
            for (var i = 0; i < count; i++)
            {
                var canonical = player.PlayerRng.Rewards.NextItem(ModelDb.CardPool<CurseCardPool>()
                    .GetUnlockedCards(player.UnlockState, player.RunState.CardMultiplayerConstraint)
                    .Where(card => card.CanBeGeneratedByModifiers))
                    ?? throw new InvalidOperationException("Super Draft has no eligible Curses.");
                var curse = player.RunState.CreateCard(canonical, player);
                CardCmd.PreviewCardPileAdd(await CardPileCmd.Add(curse, PileType.Deck));
            }
        }
    }
}
