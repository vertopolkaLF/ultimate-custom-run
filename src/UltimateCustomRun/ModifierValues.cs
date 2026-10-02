using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace UltimateCustomRun;

internal static class ModifierValues
{
    internal const string SaveKey = nameof(SpecializedDraft.CustomValue);
    internal sealed record Spec(int Min, int Max, int Step, int Default, string Unit)
    {
        internal int Normalize(int value) => Min + (int)Math.Round(
            (Math.Clamp(value, Min, Max) - Min) / (double)Step, MidpointRounding.AwayFromZero) * Step;
    }
    private sealed class ValueState { internal int Value; }
    private static readonly ConditionalWeakTable<ModifierModel, ValueState> Values = new();

    internal static Spec? For(ModifierModel? modifier) => modifier switch
    {
        Specialized or SpecializedDraft or SpecializedPickAny or AllStar or AllStarDraft or Friendship or FriendshipDraft
            => new(1, 10, 1, 5, "cards"),
        Draft or SealedDeck => new(5, 20, 5, 10, "cards"),
        Insanity => new(5, 60, 5, 30, "cards"),
        Hoarder => new(1, 5, 1, 2, "extra copies"),
        Midas => new(150, 300, 5, 200, "% gold"),
        _ => null
    };

    internal static int Get(ModifierModel modifier) => Values.TryGetValue(modifier, out var state)
        ? state.Value : For(modifier)?.Default ?? throw new ArgumentException("Modifier has no configurable value.");

    internal static void Set(ModifierModel modifier, int value)
    {
        modifier.AssertMutable();
        var spec = For(modifier) ?? throw new ArgumentException("Modifier has no configurable value.");
        Values.GetOrCreateValue(modifier).Value = spec.Normalize(value);
    }

    internal static int ForPlayer<T>(Player player) where T : ModifierModel =>
        player.RunState.Modifiers.FirstOrDefault(modifier => modifier is T) is { } modifier
            ? Get(modifier) : Get(ModelDb.Modifier<T>());

    internal static int SpecializedCount(Player player) =>
        Get(player.RunState.Modifiers.First(modifier => modifier is Specialized or SpecializedDraft or SpecializedPickAny));

    internal static int FriendshipCount(Player player) =>
        Get(player.RunState.Modifiers.First(modifier => modifier is Friendship or FriendshipDraft));

    internal static int ScaleGold(int gold, ModifierModel modifier) => (int)((long)gold * Get(modifier) / 100);

    internal static LocString WithValue(LocString original, int originalValue, int value)
    {
        var key = original.LocEntryKey + ".ultimate_value_" + value;
        var text = Regex.Replace(original.GetRawText(), @"\[blue\]" + originalValue + @"(%?)\[/blue\]",
            match => "[blue]" + value + match.Groups[1].Value + "[/blue]");
        LocManager.Instance.GetTable(original.LocTable).MergeWith(new Dictionary<string, string> { [key] = text });
        var result = new LocString(original.LocTable, key);
        result.AddVariablesFrom(original);
        return result;
    }

    internal static LocString SealedPrompt(LocString original, Player player) =>
        WithValue(original, 10, ForPlayer<SealedDeck>(player));

    [HarmonyPatch(typeof(ModifierModel), nameof(ModifierModel.ToSerializable))]
    private static class SavePatch
    {
        [HarmonyPostfix]
        private static void Postfix(ModifierModel __instance, SerializableModifier __result)
        {
            if (!Values.TryGetValue(__instance, out var state)) return;
            __result.Props ??= new SavedProperties();
            __result.Props.ints ??= [];
            __result.Props.ints.RemoveAll(prop => prop.name == SaveKey);
            __result.Props.ints.Add(new(SaveKey, state.Value));
        }
    }

    [HarmonyPatch(typeof(ModifierModel), nameof(ModifierModel.FromSerializable))]
    private static class LoadPatch
    {
        [HarmonyPostfix]
        private static void Postfix(SerializableModifier __0, ModifierModel __result)
        {
            if (For(__result) == null) return;
            var saved = __0.Props?.ints?.FirstOrDefault(prop => prop.name == SaveKey);
            if (saved is { name: SaveKey }) Set(__result, saved.Value.value);
        }
    }

    [HarmonyPatch(typeof(AbstractModel), nameof(AbstractModel.MutableClone))]
    private static class ClonePatch
    {
        [HarmonyPostfix]
        private static void Postfix(AbstractModel __instance, AbstractModel __result)
        {
            if (__instance is ModifierModel source && __result is ModifierModel target && Values.TryGetValue(source, out var state))
                Set(target, state.Value);
        }
    }

    [HarmonyPatch(typeof(ModifierModel), nameof(ModifierModel.Description), MethodType.Getter)]
    private static class DescriptionPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ModifierModel __instance, ref LocString __result)
        {
            if (For(__instance) is not { } spec || !Values.TryGetValue(__instance, out var state)) return;
            __result = WithValue(__result, spec.Default, state.Value);
        }
    }
}

// Keep the game's rewards, RNG, hooks and animations. Only replace the actual count
// in each async state machine; fail at patch time if a future game update changes it.
[HarmonyPatch]
internal static class NativeModifierCountPatch
{
    internal static readonly (Type Type, string Method, int Default)[] Targets =
    [
        (typeof(Specialized), "ObtainCards", 5), (typeof(AllStar), "ObtainCards", 5),
        (typeof(Draft), "OfferRewards", 10), (typeof(SealedDeck), "ChooseCards", 10),
        (typeof(Insanity), "ObtainCards", 30), (typeof(Hoarder), nameof(Hoarder.AfterCardChangedPiles), 2)
    ];

    private static IEnumerable<MethodBase> TargetMethods() => Targets.Select(target =>
        (MethodBase)AccessTools.AsyncMoveNext(AccessTools.Method(target.Type, target.Method)));

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var target = Targets.Single(target => AccessTools.AsyncMoveNext(AccessTools.Method(target.Type, target.Method)) == __originalMethod);
        var field = AccessTools.Field(__originalMethod.DeclaringType, target.Type == typeof(Hoarder) ? "<>4__this" : "player")
            ?? throw new InvalidOperationException("Modifier state machine no longer exposes its value owner.");
        var resolver = target.Type == typeof(Hoarder)
            ? AccessTools.Method(typeof(ModifierValues), nameof(ModifierValues.Get))
            : AccessTools.Method(typeof(ModifierValues), nameof(ModifierValues.ForPlayer)).MakeGenericMethod(target.Type);
        var replacements = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.LoadsConstant(target.Default))
            {
                replacements++;
                yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
                yield return new CodeInstruction(OpCodes.Ldfld, field);
                yield return new CodeInstruction(OpCodes.Call, resolver);
            }
            else
            {
                yield return instruction;
                if (target.Type == typeof(SealedDeck) && instruction.opcode == OpCodes.Newobj &&
                    instruction.operand is ConstructorInfo ctor && ctor.DeclaringType == typeof(LocString))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldfld, field);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ModifierValues), nameof(ModifierValues.SealedPrompt)));
                }
            }
        }
        if (replacements != 1) throw new InvalidOperationException($"{target.Type.Name} count changed: expected one value, found {replacements}.");
    }
}

[HarmonyPatch(typeof(Midas), nameof(Midas.TryModifyRewardsLate))]
internal static class MidasValuePatch
{
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var replacements = 0;
        for (var i = 0; i < code.Count; i++)
        {
            if (code[i].LoadsConstant(2) && i + 1 < code.Count && code[i + 1].opcode == OpCodes.Mul)
            {
                replacements++;
                yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(code[i]).MoveBlocksFrom(code[i]);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ModifierValues), nameof(ModifierValues.ScaleGold)))
                    .MoveLabelsFrom(code[++i]).MoveBlocksFrom(code[i]);
            }
            else yield return code[i];
        }
        if (replacements != 1) throw new InvalidOperationException("Midas gold multiplier changed.");
    }
}
