using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Rewards;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Runs;

namespace UltimateCustomRun;

internal static class SpecializedDraftReward
{
    internal const int OfferCount = 3;

    internal static int OfferCountFor(Player player) => CardSwarm.OfferCount(player, OfferCount);

    internal static CardCreationOptions Options(Player player) =>
        Options(player.Character.CardPool);

    internal static CardCreationOptions Options(CardPoolModel pool) =>
        CardCreationOptions.ForNonCombatWithUniformOdds([pool])
            .WithFlags(CardCreationFlags.NoRarityModification | CardCreationFlags.IsCardReward);

    internal static async Task ChooseAndObtain(Player player)
    {
        // Generate standard distinct rewards across all available non-basic rarities.
        var cards = CardFactory.CreateForReward(player, OfferCountFor(player), Options(player)).ToList();
        await SelectAndObtain(player, cards, ModifierValues.SpecializedCount(player));
    }

    internal static async Task SelectAndObtain(Player player, IReadOnlyList<CardCreationResult> cards, int copies = 1)
    {
        // Use the actual Card Reward screen, not the similarly styled choose-a-card screen.
        var context = new BlockingPlayerChoiceContext();
        var synchronizer = RunManager.Instance.PlayerChoiceSynchronizer;
        var choiceId = synchronizer.ReserveChoiceId(player);
        NCardRewardSelectionScreen? screen = null;
        await context.SignalPlayerChoiceBegun(player, PlayerChoiceOptions.None);
        try
        {
            int? index;
            if (LocalContext.IsMe(player))
            {
                screen = NCardRewardSelectionScreen.ShowScreen(cards, Array.Empty<CardRewardAlternative>())
                    ?? throw new InvalidOperationException("Could not open card rewards.");
                index = await screen.OptionSelected();
                synchronizer.SyncLocalChoice(player, choiceId, PlayerChoiceResult.FromIndex(index));
            }
            else
            {
                index = (await synchronizer.WaitForRemoteChoice(player, choiceId)).AsIndexOrNull();
            }
            var selected = ResolveSelection(cards, index);
            CardModel? obtained = null;
            for (var i = 0; i < copies; i++)
            {
                var card = copies == 1 ? selected : player.RunState.CloneCard(selected);
                var result = await CardPileCmd.Add(card, PileType.Deck, CardPilePosition.Bottom);
                if (result.success) obtained ??= result.cardAdded;
            }
            if (screen != null && obtained != null)
            {
                // Reuse the chosen reward node, exactly as native Draft does. Never await the flight.
                var holder = screen.GetCardHolder(selected);
                var cardNode = holder.CardNode
                    ?? throw new InvalidOperationException("Selected draft card has no visual node.");
                var run = NRun.Instance
                    ?? throw new InvalidOperationException("Draft animation requires an active run.");
                run.GlobalUi.ReparentCard(cardNode);
                cardNode.Model = obtained;
                holder.QueueFreeSafely();
                run.GlobalUi.TopBar.TrailContainer.AddChildSafely(
                    NCardFlyVfx.Create(cardNode, PileType.Deck, isAddingToPile: true, player.Character.TrailPath));
            }
        }
        finally
        {
            if (screen != null && GodotObject.IsInstanceValid(screen)) NOverlayStack.Instance?.Remove(screen);
            await context.SignalPlayerChoiceEnded();
        }
    }

    internal static CardModel ResolveSelection(IReadOnlyList<CardCreationResult> cards, int? index)
    {
        if (index == null) throw new InvalidOperationException("Starting drafts require a card selection.");
        if (index < 0 || index >= cards.Count) throw new InvalidOperationException("Invalid Specialized: Draft selection index.");
        return cards[index.Value].Card;
    }
}
