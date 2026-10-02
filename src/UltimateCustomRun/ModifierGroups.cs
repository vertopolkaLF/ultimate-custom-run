using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Modifiers;

namespace UltimateCustomRun;

internal enum ModifierGroup { ImprovedStart, Modifiers, CardPool, Negatives }

internal static class ModifierGroups
{
    internal static readonly (ModifierGroup Group, string Title)[] Sections =
    [
        (ModifierGroup.ImprovedStart, "Improved Start"),
        (ModifierGroup.Modifiers, "Modifiers"),
        (ModifierGroup.CardPool, "Card Pool"),
        (ModifierGroup.Negatives, "Negatives")
    ];

    internal static ModifierGroup Classify(ModifierModel modifier, IReadOnlySet<Type> negativeTypes)
    {
        if (negativeTypes.Contains(modifier.GetType())) return ModifierGroup.Negatives;
        if (modifier is CharacterCards or ColorlessCards) return ModifierGroup.CardPool;
        if (modifier is NeowStarterChoice or Specialized or SpecializedPickAny or SpecializedDraft or Draft or SealedDeck or Insanity or AllStar or AllStarDraft or Friendship or FriendshipDraft)
            return ModifierGroup.ImprovedStart;
        return ModifierGroup.Modifiers;
    }
}
