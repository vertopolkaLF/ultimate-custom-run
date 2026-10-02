using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;

namespace UltimateCustomRun;

internal static class SpecializedDraftReward
{
    internal const int OfferCount = 3;

    internal static CardCreationOptions Options(Player player) =>
        Options(player.Character.CardPool);

    internal static CardCreationOptions Options(CardPoolModel pool) =>
        CardCreationOptions.ForNonCombatWithUniformOdds([pool])
            .WithFlags(CardCreationFlags.NoRarityModification | CardCreationFlags.IsCardReward);

    internal static async Task ChooseAndObtain(Player player)
    {
        // Generate standard distinct rewards across all available non-basic rarities.
        var cards = CardFactory.CreateForReward(player, OfferCount, Options(player)).ToList();
        var selected = await SelectReward(player, cards);
        if (selected != null) await SpecializedCardChoices.ObtainCopies(player, selected);
    }

    internal static async Task<CardModel?> SelectReward(Player player, IReadOnlyList<CardCreationResult> cards)
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
            return ResolveSelection(cards, index);
        }
        finally
        {
            if (screen != null && GodotObject.IsInstanceValid(screen)) NOverlayStack.Instance?.Remove(screen);
            await context.SignalPlayerChoiceEnded();
        }
    }

    internal static CardModel? ResolveSelection(IReadOnlyList<CardCreationResult> cards, int? index)
    {
        if (index == null) return null; // Standard Card Reward back/skip behavior.
        if (index < 0 || index >= cards.Count) throw new InvalidOperationException("Invalid Specialized: Draft selection index.");
        return cards[index.Value].Card;
    }
}
