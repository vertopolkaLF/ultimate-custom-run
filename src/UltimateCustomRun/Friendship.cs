using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace UltimateCustomRun;

public sealed class Friendship : ModifierModel
{
    internal const string DisplayTitle = "Friendship";
    internal const string DisplayDescription = "Start with [blue]5[/blue] Co-Op cards.";
    protected override string IconPath => ImageHelper.GetImagePath("packed/modifiers/all_star.png");

    public override Func<Task>? GenerateNeowOption(EventModel eventModel) =>
        eventModel.Owner is { } player && FriendshipCardRewards.IsCoOpRun(player)
            ? () => FriendshipCardRewards.ChooseRandomCard(player)
            : null;
}

public sealed class FriendshipDraft : ModifierModel
{
    internal const string DisplayTitle = "Friendship - Draft";
    internal const string DisplayDescription = "Start with [blue]5[/blue] Co-Op card rewards.";
    protected override string IconPath => ImageHelper.GetImagePath("packed/modifiers/all_star.png");

    public override Func<Task>? GenerateNeowOption(EventModel eventModel) =>
        eventModel.Owner is { } player && FriendshipCardRewards.IsCoOpRun(player)
            ? () => FriendshipCardRewards.ChooseRewards(player)
            : null;
}

internal static class FriendshipCardRewards
{
    internal const int CardCount = 5;

    internal static bool IsCoOpRun(Player player) =>
        player.RunState.Players.Count > 1;

    internal static CardCreationOptions Options(Player player) =>
        CardCreationOptions.ForNonCombatWithUniformOdds([player.Character.CardPool])
            .WithFlags(CardCreationFlags.NoRarityModification | CardCreationFlags.IsCardReward)
            .WithFilter(card => card.MultiplayerConstraint == CardMultiplayerConstraint.MultiplayerOnly);

    internal static async Task ChooseRandomCard(Player player)
    {
        var card = CardFactory.CreateForReward(player, 1, Options(player)).Single().Card;
        await SpecializedCardChoices.ObtainCopies(player, card);
    }

    internal static async Task ChooseRewards(Player player)
    {
        for (var i = 0; i < CardCount; i++)
        {
            var offers = CardFactory.CreateForReward(player, SpecializedDraftReward.OfferCount, Options(player)).ToList();
            var selected = await SpecializedDraftReward.SelectReward(player, offers);
            if (selected == null) continue;
            var result = await CardPileCmd.Add(selected, PileType.Deck);
            CardCmd.PreviewCardPileAdd([result]);
            await Cmd.CustomScaledWait(0.6f, 1.2f);
        }
    }
}
