using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;

namespace UltimateCustomRun;

public abstract class TwentyTwentyModifier : ModifierModel
{
    protected override string IconPath => ImageHelper.GetImagePath("packed/modifiers/specialized.png");
    public override Func<Task>? GenerateNeowOption(EventModel eventModel) =>
        eventModel.Owner is { } player ? () => TwentyTwentyCards.Obtain(player, this) : null;

    public override bool TryModifyRestSiteOptions(Player player, ICollection<RestSiteOption> options) =>
        TwentyTwentyCards.AddCloneOption(player, options);
}

public sealed class TwentyTwenty : TwentyTwentyModifier
{
    internal const string DisplayTitle = "20/20";
    internal const string DisplayDescription = "Gain Pael's Growth and start with [blue]1[/blue] random card enchanted with Pael's Clone. Duplicate all Clone cards at campfires.";
}

public sealed class TwentyTwentyDraft : TwentyTwentyModifier
{
    internal const string DisplayTitle = "20/20 - Draft";
    internal const string DisplayDescription = "Gain Pael's Growth and choose [blue]1[/blue] card reward enchanted with Pael's Clone. Duplicate all Clone cards at campfires.";
}

public sealed class TwentyTwentyAny : TwentyTwentyModifier
{
    internal const string DisplayTitle = "20/20 - Any";
    internal const string DisplayDescription = "Gain Pael's Growth and choose [blue]1[/blue] card from your character's pool enchanted with Pael's Clone. Duplicate all Clone cards at campfires.";
}

internal static class TwentyTwentyCards
{
    private const string PromptKey = "ULTIMATE_CUSTOM_RUN.twentyTwentyPrompt";

    internal static bool CanOffer(CardModel card) =>
        card.Rarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare &&
        ModelDb.Enchantment<Clone>().CanEnchant(card);

    internal static CardCreationOptions Options(Player player) =>
        SpecializedDraftReward.Options(player).WithFilter(CanOffer);

    internal static bool AddCloneOption(Player player, ICollection<RestSiteOption> options)
    {
        // Pael's Growth supplies the same native option through its own hook.
        if (!player.Deck.Cards.Any(card => card.Enchantment is Clone) ||
            player.Relics.Any(relic => relic is PaelsGrowth) || options.Any(option => option is CloneRestSiteOption))
            return false;
        options.Add(new CloneRestSiteOption(player));
        return true;
    }

    internal static async Task Obtain(Player player, TwentyTwentyModifier modifier)
    {
        if (!player.Relics.Any(relic => relic is PaelsGrowth))
        {
            var relic = (PaelsGrowth)ModelDb.Relic<PaelsGrowth>().ToMutable();
            await TwentyTwentyRelic.WithoutInitialEnchantment(relic, async () => await RelicCmd.Obtain(relic, player));
        }

        for (var i = 0; i < ModifierValues.Get(modifier); i++)
        {
            CardModel? card;
            if (modifier is TwentyTwentyAny)
                card = await PickAny(player);
            else
            {
                var offers = CardFactory.CreateForReward(player,
                    modifier is TwentyTwentyDraft ? SpecializedDraftReward.OfferCountFor(player) : 1, Options(player)).ToList();
                // Show the enchantment on the actual rewards before choosing.
                foreach (var offer in offers) Enchant(offer.Card);
                card = modifier is TwentyTwentyDraft
                    ? await SpecializedDraftReward.SelectReward(player, offers) : offers.Single().Card;
            }
            if (card == null) continue;
            var result = await CardPileCmd.Add(card, PileType.Deck);
            CardCmd.PreviewCardPileAdd([result]);
            await Cmd.CustomScaledWait(0.6f, 1.2f);
        }
    }

    internal static void Enchant(CardModel card)
    {
        // Match the amount used by Pael's Growth; clones retain the native enchantment.
        CardCmd.Enchant<Clone>(card, 4m);
    }

    private static async Task<CardModel> PickAny(Player player)
    {
        var options = Options(player);
        var candidates = options.GetPossibleCards(player)
            .Where(CanOffer)
            .Where(card => player.RunState.Players.Count > 1
                ? card.MultiplayerConstraint != CardMultiplayerConstraint.SingleplayerOnly
                : card.MultiplayerConstraint != CardMultiplayerConstraint.MultiplayerOnly)
            .DistinctBy(card => card.Id).OrderBy(card => card.Id.ToString(), StringComparer.Ordinal).ToList();
        if (candidates.Count == 0) throw new InvalidOperationException("20/20 has no eligible cards.");
        LocManager.Instance.GetTable("modifiers").MergeWith(new Dictionary<string, string>
        {
            [PromptKey] = LocManager.Instance.CultureInfo.TwoLetterISOLanguageName == "ru"
                ? "Выберите карту с зачарованием Clone для копирования у костров."
                : "Choose a card enchanted with Clone to duplicate at campfires."
        });
        var previews = candidates.Select(card =>
        {
            var preview = card.ToMutable();
            preview.Owner = player;
            Enchant(preview);
            return preview;
        }).ToList();
        var prefs = new CardSelectorPrefs(new LocString("modifiers", PromptKey), 1)
        {
            Cancelable = false, RequireManualConfirmation = true, UnpoweredPreviews = true
        };
        var selected = (await CardSelectCmd.FromSimpleGrid(new BlockingPlayerChoiceContext(), previews, player, prefs)).Single();
        var result = CardFactory.CreateForReward(player, 1,
            options.WithFilter(card => card.Id == selected.Id && CanOffer(card))).Single().Card;
        Enchant(result);
        return result;
    }
}

[HarmonyPatch(typeof(PaelsGrowth), nameof(PaelsGrowth.AfterObtained))]
internal static class TwentyTwentyRelic
{
    private static readonly ConditionalWeakTable<PaelsGrowth, object> GrantedRelics = new();

    internal static async Task WithoutInitialEnchantment(PaelsGrowth relic, Func<Task> obtain)
    {
        // Suppress only this modifier's pickup effect, including across asynchronous acquisition.
        GrantedRelics.Add(relic, new object());
        try { await obtain(); }
        finally { GrantedRelics.Remove(relic); }
    }

    [HarmonyPrefix]
    internal static bool Prefix(PaelsGrowth __instance, ref Task __result)
    {
        if (!GrantedRelics.TryGetValue(__instance, out _)) return true;
        __result = Task.CompletedTask;
        return false;
    }
}
