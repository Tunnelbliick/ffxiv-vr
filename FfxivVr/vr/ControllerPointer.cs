using Silk.NET.Maths;
using System.Collections.Generic;
using System.Linq;

namespace FfxivVR;

public enum PointerHand
{
    Left,
    Right,
}

public enum PointerClick
{
    Left,
    Right,
}

public class PointerFrame
{
    public HashSet<VRButton> GamepadButtons { get; } = new();
    public List<PointerClick> Down { get; } = new();
    public List<PointerClick> Up { get; } = new();
    public PointerHand? CursorHand { get; set; }
    public PointerHand? LaserHand { get; set; }
}

public class ControllerPointer
{
    public const float MoveThreshold = 0.003f;

    private static readonly (VRButton Button, PointerHand Hand, PointerClick Click)[] Buttons = [
        (VRButton.RightTrigger, PointerHand.Right, PointerClick.Left),
        (VRButton.LeftTrigger, PointerHand.Left, PointerClick.Left),
        (VRButton.RightGrip, PointerHand.Right, PointerClick.Right),
        (VRButton.LeftGrip, PointerHand.Left, PointerClick.Right),
    ];

    private readonly HashSet<VRButton> held = new();
    private readonly HashSet<VRButton> captured = new();
    private readonly Dictionary<PointerHand, Vector2D<float>> anchors = new();
    private PointerHand? dragHand;

    public PointerFrame Update(IReadOnlySet<VRButton> pressed, Vector2D<float>? left, Vector2D<float>? right)
    {
        var frame = new PointerFrame();
        foreach (var button in pressed)
        {
            if (!Buttons.Any(b => b.Button == button))
            {
                frame.GamepadButtons.Add(button);
            }
        }
        foreach (var (button, hand, click) in Buttons)
        {
            var isPressed = pressed.Contains(button);
            if (isPressed && held.Add(button))
            {
                if (Position(hand, left, right) != null)
                {
                    if (!IsClickHeld(click))
                    {
                        frame.Down.Add(click);
                    }
                    captured.Add(button);
                    dragHand = hand;
                }
            }
            else if (!isPressed && held.Remove(button) && captured.Remove(button) && !IsClickHeld(click))
            {
                frame.Up.Add(click);
            }
            if (isPressed && !captured.Contains(button))
            {
                frame.GamepadButtons.Add(button);
            }
        }
        if (captured.Count == 0)
        {
            dragHand = null;
        }

        if (dragHand is PointerHand dragging)
        {
            if (Position(dragging, left, right) is { } position)
            {
                frame.CursorHand = dragging;
                anchors[dragging] = position;
            }
        }
        else
        {
            foreach (var hand in new[] { PointerHand.Right, PointerHand.Left })
            {
                if (Position(hand, left, right) is not { } position)
                {
                    anchors.Remove(hand);
                    continue;
                }
                if (frame.CursorHand == null && (!anchors.TryGetValue(hand, out var anchor) || (position - anchor).Length > MoveThreshold))
                {
                    frame.CursorHand = hand;
                    anchors[hand] = position;
                }
            }
        }
        frame.LaserHand = dragHand ?? (right != null ? PointerHand.Right : left != null ? PointerHand.Left : null);
        return frame;
    }

    private bool IsClickHeld(PointerClick click)
    {
        return Buttons.Any(b => b.Click == click && captured.Contains(b.Button));
    }

    private static Vector2D<float>? Position(PointerHand hand, Vector2D<float>? left, Vector2D<float>? right)
    {
        return hand == PointerHand.Left ? left : right;
    }
}