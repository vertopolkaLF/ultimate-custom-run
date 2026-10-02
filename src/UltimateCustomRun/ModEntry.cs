using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Smoke")]

namespace UltimateCustomRun;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public const string HarmonyId = "vertopolka.UltimateCustomRun";

    public static void Initialize()
    {
        ApplyPatches();
        GD.Print("[Ultimate Custom Run] Loaded: modifier groups and value sliders, custom run parameters, Neow!!, Specialized variants, All Star – Draft, Friendship variants, Rich Loot and Card Swarm.");
    }

    // Game startup discovers ModifierModel subclasses from loaded mod assemblies.
    // Registering them here would construct them again during ModelDb.Init.
    internal static void ApplyPatches() => new Harmony(HarmonyId).PatchAll(typeof(ModEntry).Assembly);
}
