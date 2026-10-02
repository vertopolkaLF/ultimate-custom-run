using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;

namespace SpecializedChoice;

// A marker modifier: normal starter relic options are restored after other modifiers.
public sealed class NeowStarterChoice : ModifierModel
{
    public const string DisplayTitle = "Neow!!";
    public const string DisplayDescription = "Brings back starter Neow relic choice";
    protected override string IconPath => ImageHelper.GetImagePath("packed/modifiers/specialized.png");
}
