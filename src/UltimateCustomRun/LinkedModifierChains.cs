using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;

namespace UltimateCustomRun;

internal static class LinkedModifierChains
{
    internal static bool AreLinked(ModifierModel first, ModifierModel second)
    {
        if (SpecializedExclusivityPatch.ShouldUntick(first, second)) return true;
        return first.GetType() != second.GetType() && ModelDb.MutuallyExclusiveModifiers.Any(group =>
            group.Any(modifier => modifier.GetType() == first.GetType()) &&
            group.Any(modifier => modifier.GetType() == second.GetType()));
    }

    internal static void Attach(Control body, IReadOnlyList<NRunModifierTickbox> rows)
    {
        var linkedPairs = Enumerable.Range(1, Math.Max(0, rows.Count - 1))
            .Where(index => rows[index - 1].Modifier is { } first && rows[index].Modifier is { } second && AreLinked(first, second))
            .ToArray();
        if (linkedPairs.Length == 0) return;

        // Same asset used by the game's linked rewards; crop its transparent padding in an atlas.
        var texture = new AtlasTexture
        {
            Atlas = ResourceLoader.Load<Texture2D>("res://images/ui/reward_screen/reward_chain.png"),
            Region = new Rect2(36, 39, 56, 51)
        };
        var overlay = new Control
        {
            Name = "ExclusiveModifierChains", MouseFilter = Control.MouseFilterEnum.Ignore,
            FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        body.AddChild(overlay);
        body.MoveChild(overlay, 0); // Draw behind the checkbox visuals, without changing VBox layout.

        foreach (var index in linkedPairs)
        {
            var previous = rows[index - 1];
            var next = rows[index];
            const string hint = "Linked modifiers: choose at most one.";
            previous.TooltipText = hint;
            next.TooltipText = hint;
            var upper = previous.GetNode<Control>("HBoxContainer/TickboxVisuals");
            var lower = next.GetNode<Control>("HBoxContainer/TickboxVisuals");
            // Reserve enough tiles for unusually tall localized descriptions; no per-frame work.
            var tiles = Enumerable.Range(0, 12).Select(_ => new TextureRect
            {
                Texture = texture, MouseFilter = Control.MouseFilterEnum.Ignore,
                FocusMode = Control.FocusModeEnum.None, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale, Rotation = -Mathf.Pi / 4,
                Modulate = new Color(1, 1, 1, 0.8f)
            }).ToArray();
            foreach (var tile in tiles) overlay.AddChild(tile);

            void Update()
            {
                if (!GodotObject.IsInstanceValid(overlay) || !GodotObject.IsInstanceValid(upper) || !GodotObject.IsInstanceValid(lower)) return;
                var toOverlay = overlay.GetGlobalTransform().AffineInverse();
                var upperCenter = toOverlay * (upper.GetGlobalTransform() * (upper.Size / 2));
                var lowerCenter = toOverlay * (lower.GetGlobalTransform() * (lower.Size / 2));
                var startY = upperCenter.Y + 21;
                var endY = lowerCenter.Y - 21;
                var span = Math.Max(0, endY - startY);
                var count = Math.Clamp((int)Math.Ceiling(span / 28), 1, tiles.Length);
                var segment = span / count;
                var size = segment / Mathf.Sqrt(2) + 2;
                for (var i = 0; i < tiles.Length; i++)
                {
                    tiles[i].Visible = i < count && span > 4;
                    if (!tiles[i].Visible) continue;
                    tiles[i].Size = new Vector2(size, size);
                    tiles[i].PivotOffset = tiles[i].Size / 2;
                    var center = new Vector2((upperCenter.X + lowerCenter.X) / 2, startY + segment * (i + 0.5f));
                    tiles[i].Position = center - tiles[i].Size / 2;
                }
            }

            void ScheduleUpdate() => Callable.From(Update).CallDeferred();
            previous.ItemRectChanged += ScheduleUpdate;
            next.ItemRectChanged += ScheduleUpdate;
            overlay.Resized += ScheduleUpdate;
            ScheduleUpdate();
        }
    }
}
