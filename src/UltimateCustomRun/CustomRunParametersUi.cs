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

namespace UltimateCustomRun;

internal static class CustomRunParametersUi
{
    private sealed class Row
    {
        internal required NRunModifierTickbox Parent;
        internal required CustomRunParameter Parameter;
        internal required HBoxContainer Root;
        internal required NSlider Slider;
        internal required Label Value;
        internal bool Applying;
    }

    private static readonly ConditionalWeakTable<NCustomRunModifiersList, List<Row>> Rows = new();
    private static readonly (CustomRunParameter Parameter, string English, string Russian, int MaxIndex)[] Specs =
    [
        (CustomRunParameter.FloorsPerAct, "Floors per act", "Этажей за акт", 23),
        (CustomRunParameter.BaseHandSize, "Base hand size", "Размер базовой руки", 11),
        (CustomRunParameter.BaseEnergy, "Base energy", "Базовая энергия", 11),
        (CustomRunParameter.EnemyHpPercent, "Enemy HP", "Здоровье врагов", 19),
        (CustomRunParameter.EnemyDamagePercent, "Enemy damage", "Урон врагов", 19),
        (CustomRunParameter.PlayerHpPercent, "Player HP", "Здоровье игрока", 19)
    ];

    internal static Control? Create(NCustomRunModifiersList list, NRunModifierTickbox parent)
    {
        if (parent.Modifier is not CustomRunParameters) return null;
        var container = new VBoxContainer
        {
            Name = "CustomRunParameterRows",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        container.AddThemeConstantOverride("separation", 2);
        foreach (var spec in Specs)
        {
            var root = new HBoxContainer
            {
                Name = spec.Parameter + "Setting",
                CustomMinimumSize = new Vector2(0, 58),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                Visible = parent.IsTicked
            };
            root.AddThemeConstantOverride("separation", 12);
            root.AddChild(new Control { CustomMinimumSize = new Vector2(52, 0), MouseFilter = Control.MouseFilterEnum.Ignore });
            var label = new Label
            {
                Text = Title(spec), CustomMinimumSize = new Vector2(220, 0),
                VerticalAlignment = VerticalAlignment.Center, SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            label.AddThemeFontOverride("font", ResourceLoader.Load<Font>("res://themes/kreon_regular_shared.tres"));
            label.AddThemeFontSizeOverride("font_size", 22);
            root.AddChild(label);

            var slider = ResourceLoader.Load<PackedScene>("res://scenes/ui/volume_slider.tscn").Instantiate<NSlider>();
            slider.Name = "ParameterSlider";
            slider.MinValue = 0;
            slider.MaxValue = spec.MaxIndex;
            slider.Step = 1;
            slider.Rounded = true;
            slider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            slider.CustomMinimumSize = new Vector2(150, 58);
            slider.FocusMode = Control.FocusModeEnum.All;
            foreach (var child in slider.FindChildren("*", "Control", true, false).OfType<Control>())
                child.MouseFilter = Control.MouseFilterEnum.Ignore;
            root.AddChild(slider);

            var value = new Label
            {
                CustomMinimumSize = new Vector2(120, 0), VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right, MouseFilter = Control.MouseFilterEnum.Ignore
            };
            value.AddThemeFontOverride("font", ResourceLoader.Load<Font>("res://themes/kreon_regular_shared.tres"));
            value.AddThemeFontSizeOverride("font_size", 22);
            root.AddChild(value);
            container.AddChild(root);

            var row = new Row { Parent = parent, Parameter = spec.Parameter, Root = root, Slider = slider, Value = value };
            Rows.GetOrCreateValue(list).Add(row);
            slider.Ready += () => Refresh(list);
            slider.ValueChanged += position =>
            {
                if (row.Applying || !Editable(list) || parent.Modifier is not CustomRunParameters modifier) return;
                CustomRunParameterValuesStore.Set(modifier, spec.Parameter, ValueAt(spec.Parameter, (int)Math.Round(position)));
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
        return container;
    }

    internal static void ApplyIncoming(NCustomRunModifiersList list, IReadOnlyCollection<ModifierModel> modifiers)
    {
        if (modifiers.OfType<CustomRunParameters>().FirstOrDefault() is not { } incoming) return;
        var tickboxes = (List<NRunModifierTickbox>)AccessTools.Field(typeof(NCustomRunModifiersList), "_modifierTickboxes").GetValue(list)!;
        foreach (var tickbox in tickboxes)
            if (tickbox.Modifier is CustomRunParameters target) CustomRunParameterValuesStore.Copy(incoming, target);
        Refresh(list);
    }

    internal static void Refresh(NCustomRunModifiersList list)
    {
        if (!Rows.TryGetValue(list, out var rows)) return;
        var russian = LocManager.Instance.CultureInfo.TwoLetterISOLanguageName == "ru";
        foreach (var row in rows)
        {
            row.Applying = true;
            try
            {
                row.Root.Visible = row.Parent.IsTicked;
                var editable = Editable(list) && row.Parent.Modifier is CustomRunParameters;
                row.Slider.MouseFilter = editable ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
                row.Slider.FocusMode = editable ? Control.FocusModeEnum.All : Control.FocusModeEnum.None;
                var actual = CustomRunParameterValuesStore.Get((CustomRunParameters)row.Parent.Modifier!).Get(row.Parameter);
                row.Value.Text = ValueText(row.Parameter, actual, russian);
                var index = IndexFor(row.Parameter, actual);
                if (row.Slider.IsNodeReady()) row.Slider.SetValueWithoutAnimation(index);
                else row.Slider.SetValueNoSignal(index);
            }
            finally { row.Applying = false; }
        }
    }

    private static bool Editable(NCustomRunModifiersList list) =>
        (MultiplayerUiMode)AccessTools.Field(typeof(NCustomRunModifiersList), "_mode").GetValue(list)!
            is MultiplayerUiMode.Singleplayer or MultiplayerUiMode.Host;

    private static string Title((CustomRunParameter Parameter, string English, string Russian, int MaxIndex) spec) =>
        LocManager.Instance.CultureInfo.TwoLetterISOLanguageName == "ru" ? spec.Russian : spec.English;

    internal static string DescriptionText(CustomRunParameterValues values, bool russian)
    {
        var lines = Specs.Where(spec => values.Get(spec.Parameter) != CustomRunParameterValues.Default.Get(spec.Parameter))
            .Select(spec => $"{(russian ? spec.Russian : spec.English)}: [gold]{ValueText(spec.Parameter, values.Get(spec.Parameter), russian)}[/gold]");
        var text = string.Join("\n", lines);
        return text.Length > 0 ? text : russian ? "Параметры по умолчанию." : "All parameters use vanilla values.";
    }

    private static string ValueText(CustomRunParameter parameter, int value, bool russian)
    {
        if (value < 0) return russian ? "По умолчанию" : "Vanilla";
        return parameter switch
        {
            CustomRunParameter.FloorsPerAct => value.ToString(),
            CustomRunParameter.BaseHandSize => value.ToString(),
            CustomRunParameter.BaseEnergy => value.ToString(),
            _ => value + "%"
        };
    }

    private static int IndexFor(CustomRunParameter parameter, int value) => parameter switch
    {
        CustomRunParameter.FloorsPerAct => value < 0 ? 0 : value - 7,
        CustomRunParameter.BaseHandSize => value < 0 ? 0 : value + 1,
        CustomRunParameter.BaseEnergy => value < 0 ? 0 : value + 1,
        _ => value / 25 - 1
    };

    private static int ValueAt(CustomRunParameter parameter, int index) => parameter switch
    {
        CustomRunParameter.FloorsPerAct => index == 0 ? -1 : index + 7,
        CustomRunParameter.BaseHandSize => index == 0 ? -1 : index - 1,
        CustomRunParameter.BaseEnergy => index == 0 ? -1 : index - 1,
        _ => (index + 1) * 25
    };
}
