using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;

namespace UltimateCustomRun;

internal static class ModifierListPatch
{
    internal static bool IsCustomOnly(ModifierModel modifier) =>
        modifier is AscensionModifier or NeowStarterChoice or SpecializedPickAny or SpecializedDraft or AllStarDraft or Friendship or FriendshipDraft or ColorlessCards or RichLoot or CardSwarm or CustomRunParameters;

    internal static IReadOnlyList<ModifierModel> ForCustomRun(IEnumerable<ModifierModel> source)
    {
        var ordered = source.Where(modifier => !IsCustomOnly(modifier)).ToList();
        var insanity = ordered.FirstOrDefault(modifier => modifier is MegaCrit.Sts2.Core.Models.Modifiers.Insanity);
        if (insanity != null)
        {
            ordered.Remove(insanity);
            var sealedDeckIndex = ordered.FindIndex(modifier => modifier is MegaCrit.Sts2.Core.Models.Modifiers.SealedDeck);
            ordered.Insert(sealedDeckIndex < 0 ? ordered.Count : sealedDeckIndex + 1, insanity);
        }
        var specializedIndex = ordered.FindIndex(modifier => modifier is MegaCrit.Sts2.Core.Models.Modifiers.Specialized);
        ordered.InsertRange(specializedIndex < 0 ? ordered.Count : specializedIndex + 1,
            [ModelDb.Modifier<SpecializedDraft>().ToMutable(), ModelDb.Modifier<SpecializedPickAny>().ToMutable()]);
        var allStarIndex = ordered.FindIndex(modifier => modifier is MegaCrit.Sts2.Core.Models.Modifiers.AllStar);
        ordered.Insert(allStarIndex < 0 ? ordered.Count : allStarIndex + 1, ModelDb.Modifier<AllStarDraft>().ToMutable());
        var allStarDraftIndex = ordered.FindIndex(modifier => modifier is AllStarDraft);
        ordered.InsertRange(allStarDraftIndex < 0 ? ordered.Count : allStarDraftIndex + 1,
            [ModelDb.Modifier<Friendship>().ToMutable(), ModelDb.Modifier<FriendshipDraft>().ToMutable()]);
        ordered.AddRange([ModelDb.Modifier<RichLoot>().ToMutable(), ModelDb.Modifier<CardSwarm>().ToMutable()]);
        ordered.Add(ModelDb.Modifier<NeowStarterChoice>().ToMutable());
        ordered.Add(ModelDb.Modifier<CustomRunParameters>().ToMutable());
        ordered.AddRange(AscensionModifiers.Create());
        // Its relic must be granted before start-of-run card reward modifiers resolve.
        ordered.Insert(0, ModelDb.Modifier<ColorlessCards>().ToMutable());
        return ordered;
    }
}

[HarmonyPatch(typeof(LocManager), nameof(LocManager.GetTable))]
internal static class ModifierTextPatch
{
    [HarmonyPostfix]
    private static void Postfix(string __0, LocTable __result)
    {
        if (__0 != "modifiers") return;
        var entry = ModelDb.GetId<NeowStarterChoice>().Entry;
        __result.MergeWith(new Dictionary<string, string>
        {
            [entry + ".title"] = NeowStarterChoice.DisplayTitle,
            [entry + ".description"] = NeowStarterChoice.DisplayDescription
        });
        __result.MergeWith(new Dictionary<string, string>
        {
            [ModelDb.GetId<SpecializedPickAny>().Entry + ".title"] = SpecializedPickAny.DisplayTitle,
            [ModelDb.GetId<SpecializedPickAny>().Entry + ".description"] = SpecializedPickAny.DisplayDescription,
            [ModelDb.GetId<SpecializedDraft>().Entry + ".title"] = SpecializedDraft.DisplayTitle,
            [ModelDb.GetId<SpecializedDraft>().Entry + ".description"] = SpecializedDraft.DisplayDescription,
            [ModelDb.GetId<AllStarDraft>().Entry + ".title"] = AllStarDraft.DisplayTitle,
            [ModelDb.GetId<AllStarDraft>().Entry + ".description"] = AllStarDraft.DisplayDescription,
            [ModelDb.GetId<Friendship>().Entry + ".title"] = Friendship.DisplayTitle,
            [ModelDb.GetId<Friendship>().Entry + ".description"] = Friendship.DisplayDescription,
            [ModelDb.GetId<FriendshipDraft>().Entry + ".title"] = FriendshipDraft.DisplayTitle,
            [ModelDb.GetId<FriendshipDraft>().Entry + ".description"] = FriendshipDraft.DisplayDescription,
            [ModelDb.GetId<ColorlessCards>().Entry + ".title"] = ColorlessCards.DisplayTitle,
            [ModelDb.GetId<ColorlessCards>().Entry + ".description"] = ColorlessCards.DisplayDescription,
            [ModelDb.GetId<RichLoot>().Entry + ".title"] = RichLoot.DisplayTitle,
            [ModelDb.GetId<RichLoot>().Entry + ".description"] = RichLoot.DisplayDescription,
            [ModelDb.GetId<CardSwarm>().Entry + ".title"] = CardSwarm.DisplayTitle,
            [ModelDb.GetId<CardSwarm>().Entry + ".description"] = CardSwarm.DisplayDescription,
            [ModelDb.GetId<CustomRunParameters>().Entry + ".title"] = CustomRunParameters.DisplayTitle,
            [ModelDb.GetId<CustomRunParameters>().Entry + ".description"] = CustomRunParameters.DisplayDescription,
            [ModifierVariantUi.NormalLabelKey] = "Normal",
            [ModifierVariantUi.DraftLabelKey] = "Draft",
            [ModifierVariantUi.DraftDescriptionKey] = "Card reward instead of random",
            [ModifierVariantUi.PickAnyLabelKey] = "Pick Any",
            [ModifierVariantUi.PickAnyDescriptionKey] = "You can pick any card"
        });
    }
}

[HarmonyPatch(typeof(NCustomRunModifiersList), "UntickMutuallyExclusiveModifiersForTickbox")]
internal static class SpecializedExclusivityPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCustomRunModifiersList __instance, NRunModifierTickbox __0)
    {
        if (!__0.IsTicked || __0.Modifier == null) return;
        var tickboxes = (List<NRunModifierTickbox>)AccessTools.Field(typeof(NCustomRunModifiersList), "_modifierTickboxes").GetValue(__instance)!;
        foreach (var other in tickboxes)
        {
            if (other.Modifier != null && ShouldUntick(__0.Modifier, other.Modifier)) other.IsTicked = false;
        }
    }

    internal static bool ShouldUntick(ModifierModel selected, ModifierModel other) =>
        selected.GetType() != other.GetType() &&
        ((IsSpecialized(selected) && IsSpecialized(other)) ||
            (IsAllStar(selected) && IsAllStar(other)) ||
            (IsFriendship(selected) && IsFriendship(other)));

    private static bool IsSpecialized(ModifierModel modifier) =>
        modifier is MegaCrit.Sts2.Core.Models.Modifiers.Specialized or SpecializedPickAny or SpecializedDraft;
    private static bool IsAllStar(ModifierModel modifier) =>
        modifier is MegaCrit.Sts2.Core.Models.Modifiers.AllStar or AllStarDraft;
    private static bool IsFriendship(ModifierModel modifier) =>
        modifier is Friendship or FriendshipDraft;
}

internal static class NeowRelicChoice
{
    private sealed class State { public bool RelicChoicesShown; }
    private static readonly ConditionalWeakTable<Neow, State> States = new();
    [ThreadStatic] private static Neow? _generatingRelicsFor;
    private static readonly MethodInfo Generate = AccessTools.Method(typeof(Neow), "GenerateInitialOptions");
    private static readonly MethodInfo SetState = AccessTools.Method(typeof(EventModel), "SetEventState");

    internal static bool IsEnabled(Neow neow) =>
        neow.Owner?.RunState.Modifiers.Any(modifier => modifier is NeowStarterChoice) == true;

    internal static bool NeedsRelicChoices(Neow neow) =>
        IsEnabled(neow) && !States.GetOrCreateValue(neow).RelicChoicesShown;

    internal static int ModifierCount(int actualCount, Neow neow) =>
        ReferenceEquals(_generatingRelicsFor, neow) ? 0 : actualCount;

    internal static IReadOnlyList<EventOption> GenerateRelicChoices(Neow neow)
    {
        var state = States.GetOrCreateValue(neow);
        var previous = _generatingRelicsFor;
        state.RelicChoicesShown = true;
        _generatingRelicsFor = neow;
        try
        {
            // Run the actual vanilla relic pool/filter/RNG logic without mutating RunState.Modifiers.
            return (IReadOnlyList<EventOption>)Generate.Invoke(neow, null)!;
        }
        catch
        {
            state.RelicChoicesShown = false;
            throw;
        }
        finally { _generatingRelicsFor = previous; }
    }

    internal static void ShowRelicChoices(Neow neow) =>
        SetState.Invoke(neow, [neow.InitialDescription, GenerateRelicChoices(neow)]);
}

[HarmonyPatch(typeof(Neow), "GenerateInitialOptions")]
internal static class NeowInitialOptionsPatch
{
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var getModifiers = AccessTools.PropertyGetter(typeof(MegaCrit.Sts2.Core.Runs.IRunState), "Modifiers");
        var changed = false;
        for (var i = 0; i < code.Count - 1; i++)
        {
            if (!code[i].Calls(getModifiers)) continue;
            if (code[i + 1].operand is not MethodInfo count || count.Name != "get_Count") continue;
            code.InsertRange(i + 2, [new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(NeowRelicChoice), nameof(NeowRelicChoice.ModifierCount)))]);
            changed = true;
            break;
        }
        if (!changed) throw new InvalidOperationException("Neow modifier-count check changed; cannot safely restore relic choices.");
        return code;
    }

    [HarmonyPostfix]
    private static void Postfix(Neow __instance, ref IReadOnlyList<EventOption> __result)
    {
        // Neow!! alone (or alongside modifiers with no Neow callback) starts with relic choices.
        if (__result.Count == 0 && NeowRelicChoice.NeedsRelicChoices(__instance))
            __result = NeowRelicChoice.GenerateRelicChoices(__instance);
    }
}

[HarmonyPatch(typeof(EventModel), "SetEventFinished")]
internal static class NeowFinishPatch
{
    [HarmonyPrefix]
    private static bool Prefix(EventModel __instance, LocString __0)
    {
        if (__instance is not Neow neow || !NeowRelicChoice.NeedsRelicChoices(neow)) return true;
        // Only intercept normal completion of the modifier chain, never death/other exits.
        if (__0.LocEntryKey != neow.Id.Entry + ".pages.DONE.description") return true;
        NeowRelicChoice.ShowRelicChoices(neow);
        return false;
    }
}
