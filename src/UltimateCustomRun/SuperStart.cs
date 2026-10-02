using System.Globalization;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;

namespace UltimateCustomRun;

public sealed class SuperDraft : ModifierModel
{
    internal const string DisplayTitle = "Super Draft";
    internal const string DisplayDescription = "Add card rewards to your starting deck until you pass. Each pick risks a Curse: [blue]0.5%[/blue], doubling each offer. At [blue]128%[/blue], gain one guaranteed Curse plus a [blue]28%[/blue] chance of another. Passing adds no Curse; each player drafts independently.";
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
                    .Where(card => card.CanBeGeneratedByModifiers));
                var curse = player.RunState.CreateCard(canonical, player);
                CardCmd.PreviewCardPileAdd(await CardPileCmd.Add(curse, PileType.Deck));
            }
        }
    }
}

public sealed class SuperSealed : ModifierModel
{
    internal const string DisplayTitle = "Super Sealed";
    internal const string DisplayDescription = "Replace your starting deck by choosing exactly [blue]15[/blue] cards from a pool of [blue]50[/blue]. Extra Card Choice does not increase this fixed pool.";
    internal const int PoolSize = 50;
    internal const int DeckSize = 15;
    internal const string PromptKey = "SUPER_SEALED.selectionPrompt";
    public override bool ClearsPlayerDeck => true;
    protected override string IconPath => ImageHelper.GetImagePath("packed/modifiers/sealed_deck.png");
    public override Func<Task>? GenerateNeowOption(EventModel eventModel) =>
        eventModel.Owner is { } player ? () => ChooseCards(player) : null;

    internal static CardCreationOptions Options(CardPoolModel pool) =>
        new CardCreationOptions([pool], CardCreationSource.Other,
            CardRarityOddsType.RegularEncounter).WithFlags(CardCreationFlags.NoUpgradeRoll |
                CardCreationFlags.ForceRarityOddsChange | CardCreationFlags.IsCardReward | CardCreationFlags.NoModifyHooks);

    private static async Task ChooseCards(Player player)
    {
        // Generate directly, outside CardReward.Populate, to keep the pool fixed at fifty.
        var pool = CardFactory.CreateForReward(player, PoolSize, Options(player.Character.CardPool)).ToList();
        if (pool.Count != PoolSize) throw new InvalidOperationException("Super Sealed requires exactly fifty offers.");
        var prefs = new CardSelectorPrefs(new LocString("modifiers", PromptKey), DeckSize)
        {
            Cancelable = false, RequireManualConfirmation = true,
            Comparison = (left, right) => left.Rarity != right.Rarity
                ? left.Rarity.CompareTo(right.Rarity)
                : string.Compare(left.Title, right.Title, LocManager.Instance.CultureInfo, CompareOptions.None)
        };
        var cards = (await CardSelectCmd.FromSimpleGridForRewards(
            new BlockingPlayerChoiceContext(), pool, player, prefs)).ToList();
        if (cards.Count != DeckSize) throw new InvalidOperationException("Super Sealed requires exactly fifteen picks.");
        CardCmd.PreviewCardPileAdd(await CardPileCmd.Add(cards, PileType.Deck), 1.2f, CardPreviewStyle.GridLayout);
        foreach (var participant in player.RunState.Players) participant.RelicGrabBag.Remove<PandorasBox>();
        player.RunState.SharedRelicGrabBag.Remove<PandorasBox>();
    }
}
