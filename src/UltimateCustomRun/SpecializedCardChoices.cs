using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;

namespace UltimateCustomRun;

internal static class SpecializedCardChoices
{
    private const string PromptKey = "ULTIMATE_CUSTOM_RUN.selectionPrompt";
    internal const int Copies = 5;

    internal static async Task PickAny(Player player)
    {
        // Match Specialized's character pool, unlock state and multiplayer restrictions.
        var options = CardCreationOptions.ForNonCombatWithUniformOdds([player.Character.CardPool])
            .WithFlags(CardCreationFlags.NoRarityModification);
        var candidates = options.GetPossibleCards(player)
            .Where(card => card.Rarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare)
            .Where(card => player.RunState.Players.Count > 1
                ? card.MultiplayerConstraint != CardMultiplayerConstraint.SingleplayerOnly
                : card.MultiplayerConstraint != CardMultiplayerConstraint.MultiplayerOnly)
            .DistinctBy(card => card.Id)
            .OrderBy(card => card.Id.ToString(), StringComparer.Ordinal)
            .ToList();

        if (candidates.Count == 0)
            throw new InvalidOperationException("Specialized – Pick Any has no eligible cards in this character's pool.");

        var loc = LocManager.Instance;
        var russian = loc.CultureInfo.TwoLetterISOLanguageName == "ru";
        loc.GetTable("modifiers").MergeWith(new Dictionary<string, string>
        {
            [PromptKey] = russian
                ? "Выберите карту. В колоду будут добавлены [blue]5[/blue] её копий."
                : "Choose a card. Add [blue]5[/blue] copies of it to your deck."
        });

        // Preview-only mutable cards are not registered in the run's card scope.
        var previews = candidates.Select(card =>
        {
            var preview = card.ToMutable();
            preview.Owner = player;
            return preview;
        }).ToList();
        var prefs = new CardSelectorPrefs(new LocString("modifiers", PromptKey), 1)
        {
            Cancelable = false,
            RequireManualConfirmation = true,
            UnpoweredPreviews = true,
            Comparison = CompareCards
        };

        // Use the same synchronized selection flow as the built-in Sealed Deck modifier.
        var selected = (await CardSelectCmd.FromSimpleGrid(
            new BlockingPlayerChoiceContext(), previews, player, prefs)).Single();

        // Keep vanilla reward creation hooks and the five-copy acquisition animation.
        var card = CardFactory.CreateForReward(player, 1,
            options.WithFilter(candidate => candidate.Id == selected.Id)).First().Card;
        await ObtainCopies(player, card);
    }

    internal static async Task ObtainCopies(Player player, CardModel card)
    {
        var results = new List<CardPileAddResult>(Copies);
        for (var i = 0; i < Copies; i++)
        {
            var copy = player.RunState.CloneCard(card);
            results.Add(await CardPileCmd.Add(copy, PileType.Deck, CardPilePosition.Bottom));
        }
        CardCmd.PreviewCardPileAdd(results, 1.2f, CardPreviewStyle.HorizontalLayout);
        await Cmd.CustomScaledWait(0.6f, 1.2f);
    }

    private static int CompareCards(CardModel left, CardModel right)
    {
        var rarity = left.Rarity.CompareTo(right.Rarity);
        return rarity != 0 ? rarity : string.Compare(left.Title, right.Title,
            LocManager.Instance.CultureInfo, System.Globalization.CompareOptions.None);
    }
}
