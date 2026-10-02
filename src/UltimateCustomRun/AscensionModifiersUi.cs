using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.sts2.Core.Nodes.TopBar;

namespace UltimateCustomRun;

// Group only the top-bar presentation; save data and independent effects stay intact.
[HarmonyPatch(typeof(NTopBar), nameof(NTopBar.Initialize))]
internal static class AscensionModifiersUi
{
    private static readonly FieldInfo ModifierField = AccessTools.Field(typeof(NTopBarModifier), "_modifier");
    private static readonly FieldInfo HoverTipField = AccessTools.Field(typeof(NTopBarModifier), "_hoverTip");

    internal static AscensionModifier[] Selected(IEnumerable<ModifierModel> modifiers) =>
        modifiers.OfType<AscensionModifier>().DistinctBy(modifier => modifier.Level)
            .OrderBy(modifier => modifier.Level).ToArray();

    internal static HoverTip CreateHoverTip(CharacterModel character, IReadOnlyList<AscensionModifier> selected)
    {
        var title = new LocString("ascension", "PORTRAIT_TITLE");
        title.Add("character", character.Title);
        title.Add("ascension", selected.Count);
        var description = new LocString("ascension", "PORTRAIT_DESCRIPTION");
        description.Add("ascensions", selected.Select(modifier => modifier.Title.GetFormattedText()).ToList());
        return new HoverTip(title, description);
    }

    [HarmonyPostfix]
    private static void Postfix(NTopBar __instance, IRunState runState)
    {
        var selected = Selected(runState.Modifiers);
        if (selected.Length == 0) return;

        var container = __instance.GetNode<Control>("%Modifiers");
        var icons = container.GetChildren().OfType<NTopBarModifier>()
            .Where(icon => ModifierField.GetValue(icon) is AscensionModifier).ToArray();
        if (icons.Length == 0) return;

        HoverTipField.SetValue(icons[0], CreateHoverTip(LocalContext.GetMe(runState)!.Character, selected));
        // Remove before the native deferred navigation update to avoid invisible focus targets.
        foreach (var icon in icons.Skip(1))
        {
            container.RemoveChild(icon);
            icon.QueueFree();
        }
    }
}
