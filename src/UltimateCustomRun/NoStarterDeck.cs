using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;

namespace UltimateCustomRun;

// The native ModifierModel.OnRunCreated clears every player's deck for ClearsPlayerDeck modifiers,
// exactly like Draft, Sealed Deck and Insanity, without offering any replacement cards.
public sealed class NoStarterDeck : ModifierModel
{
    internal const string DisplayTitle = "No Starting Deck";
    internal const string DisplayDescription = "Start the run with no cards in your deck.";
    public override bool ClearsPlayerDeck => true;
    protected override string IconPath => ImageHelper.GetImagePath("packed/modifiers/draft.png");
}
