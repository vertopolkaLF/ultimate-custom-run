using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Platform;

namespace UltimateCustomRun;

internal static class CoopHandicapUi
{
    private sealed class PlayerRows
    {
        internal required VBoxContainer Root;
        internal required Label Caption;
    }

    private sealed class Row
    {
        internal required NRunModifierTickbox Parent;
        internal required int Slot;
        internal required HandicapStat Stat;
        internal required NSlider Slider;
        internal required Label Value;
        internal bool Applying;
    }

    private sealed class State
    {
        internal required NRunModifierTickbox Parent;
        internal required VBoxContainer Container;
        internal readonly List<PlayerRows> Players = [];
        internal readonly List<Row> Rows = [];
    }

    private static readonly ConditionalWeakTable<NCustomRunModifiersList, State> States = new();
    private static int MaxIndex => (CoopHandicap.PercentSpec.Max - CoopHandicap.PercentSpec.Min) / CoopHandicap.PercentSpec.Step;

    internal static Control? Create(NCustomRunModifiersList list, NRunModifierTickbox parent)
    {
        if (parent.Modifier is not CoopHandicap) return null;
        var container = new VBoxContainer { Name = "CoopHandicapRows", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        container.AddThemeConstantOverride("separation", 2);
        var state = new State { Parent = parent, Container = container };
        for (var slot = 0; slot < CoopHandicap.MaxPlayers; slot++)
        {
            var group = new VBoxContainer { Name = "Player" + (slot + 1), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            group.AddThemeConstantOverride("separation", 0);
            var captionRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            captionRow.AddChild(new Control { CustomMinimumSize = new Vector2(52, 0), MouseFilter = Control.MouseFilterEnum.Ignore });
            var caption = new Label { MouseFilter = Control.MouseFilterEnum.Ignore, ClipText = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            caption.AddThemeFontOverride("font", ResourceLoader.Load<Font>("res://themes/kreon_bold_shared.tres"));
            caption.AddThemeFontSizeOverride("font_size", 22);
            caption.AddThemeColorOverride("font_color", new Color(0.937f, 0.784f, 0.318f));
            captionRow.AddChild(caption);
            group.AddChild(captionRow);
            foreach (var stat in Enum.GetValues<HandicapStat>())
            {
                var (root, _, slider, value) = CustomRunParametersUi.BuildSliderRow(
                    stat.ToString() + "Setting", StatTitle(stat), MaxIndex, true);
                group.AddChild(root);
                var row = new Row { Parent = parent, Slot = slot, Stat = stat, Slider = slider, Value = value };
                state.Rows.Add(row);
                slider.Ready += () => Refresh(list);
                slider.ValueChanged += position =>
                {
                    if (row.Applying || !Editable(list) || parent.Modifier is not CoopHandicap modifier) return;
                    modifier.Set(stat, row.Slot, CoopHandicap.PercentSpec.Min + (int)Math.Round(position) * CoopHandicap.PercentSpec.Step);
                    Refresh(list);
                    list.EmitSignal(NCustomRunModifiersList.SignalName.ModifiersChanged);
                };
                slider.GuiInput += input =>
                {
                    if (!Editable(list)) return;
                    if (input.IsActionPressed(MegaInput.left)) { slider.Value--; slider.AcceptEvent(); }
                    if (input.IsActionPressed(MegaInput.right)) { slider.Value++; slider.AcceptEvent(); }
                };
            }
            container.AddChild(group);
            state.Players.Add(new PlayerRows { Root = group, Caption = caption });
        }
        States.AddOrUpdate(list, state);
        return container;
    }

    internal static void ApplyIncoming(NCustomRunModifiersList list, IReadOnlyCollection<ModifierModel> modifiers)
    {
        if (modifiers.OfType<CoopHandicap>().FirstOrDefault() is not { } incoming) return;
        var tickboxes = (List<NRunModifierTickbox>)AccessTools.Field(typeof(NCustomRunModifiersList), "_modifierTickboxes").GetValue(list)!;
        foreach (var tickbox in tickboxes)
            if (tickbox.Modifier is CoopHandicap target && !ReferenceEquals(target, incoming)) target.CopyFrom(incoming);
        Refresh(list);
    }

    internal static void Refresh(NCustomRunModifiersList list)
    {
        if (!GodotObject.IsInstanceValid(list) || !States.TryGetValue(list, out var state) ||
            state.Parent.Modifier is not CoopHandicap modifier) return;
        var russian = LocManager.Instance.CultureInfo.TwoLetterISOLanguageName == "ru";
        var lobby = FindScreen(list)?.Lobby;
        // Show one block per lobby player; before the lobby exists, show every slot.
        var shown = lobby != null && lobby.Players.Count > 0 ? lobby.Players.Count : CoopHandicap.MaxPlayers;
        state.Container.Visible = state.Parent.IsTicked;
        for (var slot = 0; slot < state.Players.Count; slot++)
        {
            var player = state.Players[slot];
            player.Root.Visible = slot < shown;
            var name = lobby != null && slot < lobby.Players.Count
                ? PlatformUtil.GetPlayerNameRaw(lobby.NetService.Platform, lobby.Players[slot].id) : null;
            var number = (russian ? "Игрок " : "Player ") + (slot + 1);
            player.Caption.Text = string.IsNullOrWhiteSpace(name) ? number : $"{number}: {name}";
        }
        var editable = Editable(list);
        foreach (var row in state.Rows)
        {
            row.Applying = true;
            try
            {
                row.Slider.MouseFilter = editable ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
                row.Slider.FocusMode = editable ? Control.FocusModeEnum.All : Control.FocusModeEnum.None;
                var value = modifier.Get(row.Stat, row.Slot);
                row.Value.Text = value + "%";
                var index = (value - CoopHandicap.PercentSpec.Min) / CoopHandicap.PercentSpec.Step;
                if (row.Slider.IsNodeReady()) row.Slider.SetValueWithoutAnimation(index);
                else row.Slider.SetValueNoSignal(index);
            }
            finally { row.Applying = false; }
        }
        ModifierValueUi.UpdateDescription(state.Parent);
    }

    private static string StatTitle(HandicapStat stat)
    {
        var russian = LocManager.Instance.CultureInfo.TwoLetterISOLanguageName == "ru";
        return stat == HandicapStat.DamageDealt
            ? russian ? "Наносимый урон" : "Damage dealt"
            : russian ? "Получаемый урон" : "Damage taken";
    }

    private static NCustomRunScreen? FindScreen(Node node)
    {
        for (var current = node.GetParent(); current != null; current = current.GetParent())
            if (current is NCustomRunScreen screen) return screen;
        return null;
    }

    private static bool Editable(NCustomRunModifiersList list) =>
        (MultiplayerUiMode)AccessTools.Field(typeof(NCustomRunModifiersList), "_mode").GetValue(list)!
            is MultiplayerUiMode.Singleplayer or MultiplayerUiMode.Host;

    // Player captions and visible slots follow the lobby as players join or leave.
    [HarmonyPatch(typeof(NCustomRunScreen))]
    private static class LobbyPlayersPatch
    {
        private static IEnumerable<System.Reflection.MethodBase> TargetMethods() =>
            new[] { nameof(NCustomRunScreen.PlayerConnected), nameof(NCustomRunScreen.PlayerChanged), nameof(NCustomRunScreen.RemotePlayerDisconnected) }
                .Select(name => (System.Reflection.MethodBase)AccessTools.Method(typeof(NCustomRunScreen), name));

        [HarmonyPostfix]
        private static void Postfix(NCustomRunModifiersList ____modifiersList) => Refresh(____modifiersList);
    }
}
