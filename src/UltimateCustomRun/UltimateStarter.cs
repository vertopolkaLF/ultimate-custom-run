using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Runs;

namespace UltimateCustomRun;

public sealed class UltimateStarter : ModifierModel
{
    internal const string DisplayTitle = "Ultimate Starter";
    internal const string DisplayDescription = "Start with [blue]3[/blue] Ultimate Strikes and Defends instead of normal starter cards.";
    internal const int Copies = 3;
    protected override string IconPath => ImageHelper.GetImagePath("packed/modifiers/specialized.png");

    internal static void ReplaceStarters(RunState run)
    {
        if (!run.Modifiers.Any(modifier => modifier is UltimateStarter)) return;
        foreach (var player in run.Players) ReplaceStarters(run, player);
    }

    internal static void ReplaceStarters(RunState run, Player player)
    {
        var basics = player.Deck.Cards.Where(card => card.IsBasicStrikeOrDefend).ToArray();
        if (basics.Length == 0) return;
        // This is starting-inventory construction, before Neow and acquisition hooks.
        // Use the same basic-card predicate as Pandora's Box, preserving special cards.
        foreach (var card in basics)
        {
            player.Deck.RemoveInternal(card, silent: true);
            card.RemoveFromState();
            run.RemoveCard(card);
        }
        for (var index = 0; index < Copies; index++) Add<UltimateStrike>(run, player);
        for (var index = 0; index < Copies; index++) Add<UltimateDefend>(run, player);
    }

    private static void Add<T>(RunState run, Player player) where T : CardModel
    {
        var card = run.CreateCard<T>(player);
        card.FloorAddedToDeck = 1;
        player.Deck.AddInternal(card, silent: true);
    }
}

// Only new-run construction is patched. Saved decks load exactly as serialized.
// Native whole-deck replacements (Draft, Sealed Deck, Insanity) still run afterward.
[HarmonyPatch(typeof(RunState), nameof(RunState.CreateForNewRun))]
internal static class UltimateStarterNewRunPatch
{
    [HarmonyPostfix]
    private static void Postfix(RunState __result) => UltimateStarter.ReplaceStarters(__result);
}
