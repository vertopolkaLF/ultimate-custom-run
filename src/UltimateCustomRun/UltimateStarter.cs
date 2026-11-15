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

    private static bool IsActive(RunState run) => run.Modifiers.Any(modifier => modifier is UltimateStarter);

    internal static void ReplaceStarters(RunState run)
    {
        if (!IsActive(run)) return;
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
        TopUp(run, player);
    }

    // Whole-deck modifiers (Draft, Sealed Deck, Insanity) wipe the starting deck in
    // OnRunCreated and build a new one at Neow. Restore the Ultimate cards right after
    // the wipe so the Neow choice adds its cards on top of them.
    internal static void RestoreAfterDeckClear(ModifierModel clearing, RunState run)
    {
        if (!clearing.ClearsPlayerDeck || !IsActive(run)) return;
        foreach (var player in run.Players) TopUp(run, player);
    }

    private static void TopUp(RunState run, Player player)
    {
        for (var index = player.Deck.Cards.OfType<UltimateStrike>().Count(); index < Copies; index++) Add<UltimateStrike>(run, player);
        for (var index = player.Deck.Cards.OfType<UltimateDefend>().Count(); index < Copies; index++) Add<UltimateDefend>(run, player);
    }

    private static void Add<T>(RunState run, Player player) where T : CardModel
    {
        var card = run.CreateCard<T>(player);
        card.FloorAddedToDeck = 1;
        player.Deck.AddInternal(card, silent: true);
    }
}

// Only new-run construction is patched. Saved decks load exactly as serialized.
[HarmonyPatch(typeof(RunState), nameof(RunState.CreateForNewRun))]
internal static class UltimateStarterNewRunPatch
{
    [HarmonyPostfix]
    private static void Postfix(RunState __result) => UltimateStarter.ReplaceStarters(__result);
}

// OnRunCreated runs only for brand new runs; loaded runs use OnRunLoaded.
[HarmonyPatch(typeof(ModifierModel), nameof(ModifierModel.OnRunCreated))]
internal static class UltimateStarterDeckClearPatch
{
    [HarmonyPostfix]
    private static void Postfix(ModifierModel __instance, RunState runState) =>
        UltimateStarter.RestoreAfterDeckClear(__instance, runState);
}
