using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;

namespace UltimateCustomRun;

// Blitz uses a fixed linear map (Ancient, 2 Monsters, Merchant, Elite, Treasure, Rest Site, Boss),
// rewards without gold, and only the Blitz Merchant. Modifiers that depend on anything else do nothing there.
internal static class BlitzCompatibility
{
    internal static bool IsDisabled(ModifierModel? modifier) => modifier is
        // Map generation, paths, and ? rooms: the Blitz map is fixed and linear.
        Flight or DeadlyEvents or BigGameHunter or SwarmingElites or
        // Gold and the normal Merchant: Blitz grants no gold and has no normal Merchant.
        Midas or Poverty or Inflation or
        // Card rarity odds: Blitz rewards use fixed rarities and never roll upgrades.
        Scarcity or
        // Every Blitz Merchant is already a Blitz Merchant.
        BlitzMerchantRandom;

    // Shown as enabled in Blitz because the mode always has this effect; only its pick count applies.
    internal static bool IsForced(ModifierModel? modifier) => modifier is BlitzMerchant;

    internal static bool IsParameterDisabled(CustomRunParameter parameter) => parameter == CustomRunParameter.FloorsPerAct;

    internal static GameMode GameModeOf(NCustomRunModifiersList list)
    {
        for (var node = list.GetParent(); node != null; node = node.GetParent())
            if (node is NCustomRunScreen screen)
                return screen.Lobby is { } lobby ? lobby.GameMode : GameMode.Standard;
        return GameMode.Standard;
    }

    internal static bool IsBlitz(NCustomRunModifiersList list) =>
        GodotObject.IsInstanceValid(list) && GameModeOf(list) == GameMode.Blitz;

    internal static IReadOnlyList<ModifierModel> ForRun(IReadOnlyList<ModifierModel> modifiers, GameMode gameMode) =>
        gameMode == GameMode.Blitz && modifiers.Any(IsDisabled) ? modifiers.Where(modifier => !IsDisabled(modifier)).ToList() : modifiers;
}

// Final guard for presets, co-op lobbies, and selections made before switching to Blitz.
[HarmonyPatch(typeof(RunState), nameof(RunState.CreateForNewRun))]
internal static class BlitzRunModifiersPatch
{
    [HarmonyPrefix]
    private static void Prefix(ref IReadOnlyList<ModifierModel> modifiers, GameMode gameMode) =>
        modifiers = BlitzCompatibility.ForRun(modifiers, gameMode);
}

// Native Blitz already uses the Blitz Merchant; Blitz Merchant only sets the number of picks there.
[HarmonyPatch(typeof(MerchantInventory), nameof(MerchantInventory.CreateForBlitzMerchant))]
internal static class BlitzMerchantPicksPatch
{
    [HarmonyPostfix]
    private static void Postfix(Player player, MerchantInventory __result)
    {
        if (player.RunState.GameMode == GameMode.Blitz && BlitzMerchantModifier.For(player.RunState) is BlitzMerchant modifier)
            __result.BlitzChoicesRemaining = modifier.Picks;
    }
}

[HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen.GameModeChanged))]
internal static class BlitzGameModeChangedPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCustomRunScreen __instance)
    {
        var list = (NCustomRunModifiersList)AccessTools.Field(typeof(NCustomRunScreen), "_modifiersList").GetValue(__instance)!;
        if (!GodotObject.IsInstanceValid(list)) return;
        ModifierGroupsUi.ApplyMode(list);
        ModifierValueUi.Refresh(list);
        // Ticks changed without the list signal; push the visible selection to the lobby and co-op clients.
        if (__instance.Lobby is { } lobby && lobby.NetService.Type is NetGameType.Singleplayer or NetGameType.Host)
            lobby.SetModifiers(list.GetModifiersTickedOn());
    }
}
