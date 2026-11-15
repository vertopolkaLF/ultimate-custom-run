using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Runs;

namespace UltimateCustomRun;

// The normal run's Random character button, added to the Custom Run character row. Each player picks it for
// themselves; the lobby resolves every Random player to a real character when the run begins, as in a normal run.
internal static class CustomRunRandomCharacter
{
    private const string ButtonScene = "res://scenes/screens/char_select/char_select_button.tscn";
    private static readonly ConditionalWeakTable<NCustomRunScreen, NCharacterSelectButton> Buttons = new();

    internal static void Add(NCustomRunScreen screen)
    {
        if (Buttons.TryGetValue(screen, out _)) return;
        var container = screen.GetNode<Control>("LeftContainer/CharSelectButtons/ButtonContainer");
        var button = ResourceLoader.Load<PackedScene>(ButtonScene).Instantiate<NCharacterSelectButton>();
        button.Name = "random_button";
        container.AddChild(button);
        button.InitAsRandom(screen);
        Buttons.Add(screen, button);

        // Vanilla links the character row without wrapping; append Random after the last character.
        var seedInput = screen.GetNode<LineEdit>("%SeedInput");
        var previous = container.GetChildren().OfType<NCharacterSelectButton>().LastOrDefault(item => item != button);
        if (previous != null)
        {
            previous.FocusNeighborRight = button.GetPath();
            button.FocusNeighborLeft = previous.GetPath();
            button.FocusNeighborTop = previous.FocusNeighborTop;
        }
        else button.FocusNeighborTop = seedInput.GetPath();
        button.FocusNeighborRight = button.GetPath();
        button.FocusNeighborBottom = button.GetPath();
        UpdateAvailability(screen);
    }

    // Random draws from every character, so (as in a normal run) it needs all characters unlocked.
    // Dread rejects a Random character at run start.
    internal static void UpdateAvailability(NCustomRunScreen screen)
    {
        if (!Buttons.TryGetValue(screen, out var button) || !GodotObject.IsInstanceValid(button)) return;
        var dread = screen.Lobby?.GameMode == GameMode.Dread;
        button.Visible = !button.IsLocked && !dread;
        if (button.Visible || !button.IsSelected) return;
        var fallback = button.GetParent().GetChildren().OfType<NCharacterSelectButton>()
            .FirstOrDefault(item => item != button && !item.IsLocked);
        fallback?.Select();
    }

    [HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen._Ready))]
    private static class ReadyPatch
    {
        [HarmonyPostfix]
        private static void Postfix(NCustomRunScreen __instance)
        {
            try { Add(__instance); }
            catch (Exception ex) { GD.PushError($"[Ultimate Custom Run] Could not add the Random character button: {ex}"); }
        }
    }

    [HarmonyPatch(typeof(NCustomRunScreen), "AfterInitialized")]
    private static class InitializedPatch
    {
        [HarmonyPostfix]
        private static void Postfix(NCustomRunScreen __instance) => UpdateAvailability(__instance);
    }

    [HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen.GameModeChanged))]
    private static class GameModePatch
    {
        [HarmonyPostfix]
        private static void Postfix(NCustomRunScreen __instance) => UpdateAvailability(__instance);
    }

    // The Custom Run screen plays the character's select sound, which Random does not have.
    [HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen.SelectCharacter))]
    private static class SelectPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(NCustomRunScreen __instance, NCharacterSelectButton __0)
        {
            if (!__0.IsRandom || __instance.Lobby == null) return true;
            AccessTools.Field(typeof(NCustomRunScreen), "_selectedButton").SetValue(__instance, __0);
            foreach (var item in __0.GetParent().GetChildren().OfType<NCharacterSelectButton>())
                if (item != __0) item.Deselect();
            __instance.Lobby.SetLocalCharacter(__0.Character);
            ((Control)AccessTools.Field(typeof(NCustomRunScreen), "_gameModeDropdown").GetValue(__instance)!)
                .FocusNeighborBottom = __0.GetPath();
            return false;
        }
    }
}
