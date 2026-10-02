using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace UltimateCustomRun;

// Submodifiers are saved/networked like normal modifiers, but require a parent.
public abstract class SubmodifierModel : ModifierModel
{
    public abstract Type ParentType { get; }
    internal bool HasParent(IEnumerable<ModifierModel> modifiers) =>
        modifiers.Any(modifier => modifier.GetType() == ParentType);
}

public sealed class CampfiresBetweenBosses : SubmodifierModel
{
    internal const string DisplayTitle = "Campfires between bosses";
    internal const string DisplayDescription = "Add a Rest Site between each pair of Double Trouble Bosses.";
    public override Type ParentType => typeof(DoubleTrouble);
    protected override string IconPath => ImageHelper.GetImagePath("atlases/ui_atlas.sprites/map/icons/map_rest.tres");
    internal static bool IsEnabled(IRunState state) => state.Modifiers.OfType<CampfiresBetweenBosses>()
        .Any(modifier => modifier.HasParent(state.Modifiers));
}
