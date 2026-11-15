using Godot;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace UltimateCustomRun;

// One native slider track with two handles for an integer range 0..Max.
// The game's NSlider only draws the track and the first handle; this class owns input and handle placement.
internal sealed class RangeSlider
{
    internal const string FocusMeta = "ucr_range_slider";
    private const string SliderScene = "res://scenes/ui/volume_slider.tscn";

    internal Control Root { get; }
    internal int Low { get; private set; }
    internal int High { get; private set; }
    internal event Action<int, int>? Changed;

    private readonly NSlider track;
    private Control? lowHandle;
    private Control? highHandle;
    private int max;
    private bool? dragging; // true while dragging the high handle, false for the low one.
    private bool highActive; // Keyboard and controller input moves this handle; select switches it.

    internal RangeSlider(string name, int max)
    {
        this.max = Math.Max(1, max);
        Root = new Control
        {
            Name = name, CustomMinimumSize = new Vector2(220, 58), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.All, MouseFilter = Control.MouseFilterEnum.Stop
        };
        Root.SetMeta(FocusMeta, true);
        track = ResourceLoader.Load<PackedScene>(SliderScene).Instantiate<NSlider>();
        track.Name = "Track";
        track.MinValue = 0;
        track.MaxValue = this.max;
        // The scene's own minimum width would push the track past Root, where clicks are not received.
        track.CustomMinimumSize = Vector2.Zero;
        track.FocusMode = Control.FocusModeEnum.None;
        track.MouseFilter = Control.MouseFilterEnum.Ignore;
        foreach (var child in track.FindChildren("*", "Control", true, false).OfType<Control>())
            child.MouseFilter = Control.MouseFilterEnum.Ignore;
        Root.AddChild(track);
        track.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        track.Ready += () =>
        {
            // NSlider eases its own handle toward Value every frame; placement is ours instead.
            track.SetProcess(false);
            lowHandle = track.GetNode<Control>("%Handle");
            highHandle = (Control)lowHandle.Duplicate();
            highHandle.Name = "HighHandle";
            highHandle.UniqueNameInOwner = false;
            lowHandle.GetParent().AddChild(highHandle);
            Place();
        };
        track.Resized += Place;
        Root.Resized += () => Callable.From(Place).CallDeferred();

        Root.GuiInput += OnInput;
        Root.FocusEntered += Place;
        Root.FocusExited += () => { dragging = null; Place(); };
    }

    internal void SetMax(int value)
    {
        max = Math.Max(1, value);
        track.MaxValue = max;
        SetValues(Low, High, notify: false);
    }

    internal void SetValues(int low, int high, bool notify)
    {
        low = Math.Clamp(low, 0, max);
        high = Math.Clamp(high, 0, max);
        if (low > high) (low, high) = (high, low);
        var changed = low != Low || high != High;
        Low = low;
        High = high;
        Place();
        if (notify && changed) Changed?.Invoke(Low, High);
    }

    private void OnInput(InputEvent input)
    {
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            if (button.Pressed)
            {
                var value = ValueAt(button.Position.X);
                // Grab the nearer handle; on a tie, the side of the click decides.
                var high = Math.Abs(value - High) < Math.Abs(value - Low) ||
                    (Math.Abs(value - High) == Math.Abs(value - Low) && value >= High);
                dragging = high;
                highActive = high;
                Root.GrabFocus();
                Drag(value);
            }
            else dragging = null;
            Root.AcceptEvent();
            return;
        }
        if (input is InputEventMouseMotion motion && dragging != null)
        {
            Drag(ValueAt(motion.Position.X));
            Root.AcceptEvent();
            return;
        }
        if (input.IsActionPressed(MegaInput.left)) { Step(-1); Root.AcceptEvent(); }
        else if (input.IsActionPressed(MegaInput.right)) { Step(1); Root.AcceptEvent(); }
        else if (input.IsActionPressed(MegaInput.select))
        {
            highActive = !highActive;
            Place();
            Root.AcceptEvent();
        }
    }

    // A dragged handle stops at the other one instead of swapping.
    private void Drag(int value)
    {
        if (dragging == true) SetValues(Low, Math.Max(value, Low), notify: true);
        else SetValues(Math.Min(value, High), High, notify: true);
    }

    private void Step(int delta)
    {
        if (highActive) SetValues(Low, Math.Max(Low, High + delta), notify: true);
        else SetValues(Math.Min(High, Low + delta), High, notify: true);
    }

    // Root coordinates mapped onto the track, the same space the handles are placed in.
    private int ValueAt(float x) =>
        track.Size.X <= 0 ? 0 : Math.Clamp((int)Math.Round((x - track.Position.X) / track.Size.X * max), 0, max);

    private void Place()
    {
        if (lowHandle == null || highHandle == null || !GodotObject.IsInstanceValid(track)) return;
        Position(lowHandle, Low);
        Position(highHandle, High);
        // While focused, dim the handle that keyboard and controller input does not move.
        var focused = Root.HasFocus() && NControllerManager.Instance?.IsUsingDirectionalNavigation == true;
        lowHandle.Modulate = focused && highActive ? new Color(1, 1, 1, 0.6f) : Colors.White;
        highHandle.Modulate = focused && !highActive ? new Color(1, 1, 1, 0.6f) : Colors.White;
        // Draw the active handle on top when both share a position.
        highHandle.GetParent().MoveChild(highActive ? highHandle : lowHandle, -1);
    }

    private void Position(Control handle, int value) =>
        handle.Position = new Vector2(track.Size.X * value / max - handle.Size.X * 0.5f, (track.Size.Y - handle.Size.Y) * 0.5f);
}
