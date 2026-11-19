using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace UltimateCustomRun;

// Development shortcut: launching the game with --ucr-custom-run puts the window on the screen given by
// --ucr-screen (windowed, without touching the saved display settings) and opens the singleplayer Custom Run
// screen as soon as the main menu is ready. Without the flag nothing changes.
[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
internal static class DevLaunch
{
    internal const string Flag = "--ucr-custom-run";
    private static bool done;

    internal static string[] Args => OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()).ToArray();

    [HarmonyPostfix]
    private static void Postfix(NMainMenu __instance)
    {
        if (done || !Args.Contains(Flag)) return;
        done = true;
        PlaceWindow();
        // Let the menu finish its own setup (fade, popups) first.
        __instance.GetTree().CreateTimer(0.5).Timeout += () =>
        {
            if (!GodotObject.IsInstanceValid(__instance) || !__instance.IsInsideTree()) return;
            try
            {
                __instance.OpenSingleplayerSubmenu();
                var stack = __instance.SubmenuStack;
                var screen = stack.GetSubmenuType<NCustomRunScreen>();
                screen.InitializeSingleplayer();
                stack.Push(screen);
            }
            catch (Exception exception)
            {
                GD.PrintErr($"[Ultimate Custom Run] Could not open Custom Run from {Flag}: {exception}");
            }
        };
    }

    // The game applies its saved display settings (fullscreen on the primary screen) during startup;
    // with the flag, the window goes to the requested screen instead.
    [HarmonyPatch(typeof(NGame), nameof(NGame.ApplyDisplaySettings))]
    private static class KeepWindowPlacement
    {
        [HarmonyPrefix]
        private static bool Prefix()
        {
            if (!Args.Contains(Flag)) return true;
            PlaceWindow();
            return false;
        }
    }

    // Skips the studio logo animation, the same as the game's own "skip intro" setting.
    [HarmonyPatch(typeof(NGame), "LaunchMainMenu")]
    private static class SkipLogo
    {
        [HarmonyPrefix]
        private static void Prefix(ref bool skipLogo)
        {
            if (Args.Contains(Flag)) skipLogo = true;
        }
    }

    private static void PlaceWindow()
    {
        try
        {
            var args = Args;
            // Godot consumes its own --screen argument, so the mod reads a copy of it.
            var index = Array.IndexOf(args, "--ucr-screen");
            var screen = index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var parsed)
                ? Math.Clamp(parsed, 0, DisplayServer.GetScreenCount() - 1)
                : DisplayServer.WindowGetCurrentScreen();
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            var area = DisplayServer.ScreenGetUsableRect(screen);
            var size = new Vector2I((int)(area.Size.X * 0.85f), (int)(area.Size.Y * 0.85f));
            DisplayServer.WindowSetSize(size);
            DisplayServer.WindowSetPosition(area.Position + (area.Size - size) / 2);
        }
        catch (Exception exception)
        {
            GD.PrintErr($"[Ultimate Custom Run] Could not place the window for {Flag}: {exception}");
        }
    }
}
