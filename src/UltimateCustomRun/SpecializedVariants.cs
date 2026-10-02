using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;

namespace UltimateCustomRun;

public sealed class SpecializedPickAny : ModifierModel
{
    internal const string DisplayTitle = "Specialized - Pick Any";
    internal const string DisplayDescription = "Choose any available card from your character's pool. Add [blue]5[/blue] copies to your starting deck.";
    protected override string IconPath => ImageHelper.GetImagePath("packed/modifiers/specialized.png");
    public override Func<Task>? GenerateNeowOption(EventModel eventModel) =>
        eventModel.Owner is { } player ? () => SpecializedCardChoices.PickAny(player) : null;
}

public sealed class SpecializedDraft : ModifierModel
{
    // Registers this name in the game's shared save/network property cache, also
    // used by the configurable vanilla modifiers without changing their model IDs.
    [MegaCrit.Sts2.Core.Saves.Runs.SavedProperty]
    public int CustomValue { get => ModifierValues.Get(this); set => ModifierValues.Set(this, value); }

    // Register the second native Sealed Deck property's name for network SavedProperties.
    [MegaCrit.Sts2.Core.Saves.Runs.SavedProperty]
    public int SealedPoolSize { get; set; } = 30;

    internal const string DisplayTitle = "Specialized - Draft";
    internal const string DisplayDescription = "Choose [blue]1[/blue] card reward. Add [blue]5[/blue] copies yo your starting deck.";
    protected override string IconPath => ImageHelper.GetImagePath("packed/modifiers/specialized.png");
    public override Func<Task>? GenerateNeowOption(EventModel eventModel) =>
        eventModel.Owner is { } player ? () => SpecializedDraftReward.ChooseAndObtain(player) : null;
}
