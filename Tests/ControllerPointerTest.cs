using Silk.NET.Maths;

namespace FfxivVR.Tests;

public class ControllerPointerTests
{
    private static readonly Vector2D<float> OnPanel = new(0.5f, 0.5f);

    private static HashSet<VRButton> Pressed(params VRButton[] buttons)
    {
        return new HashSet<VRButton>(buttons);
    }

    [Test]
    public void TriggerClicksWhilePointingAtTheUI()
    {
        var pointer = new ControllerPointer();
        var press = pointer.Update(Pressed(VRButton.RightTrigger, VRButton.A), null, OnPanel);
        Assert.That(press.Down, Is.EqualTo(new[] { PointerClick.Left }));
        Assert.That(press.GamepadButtons, Is.EquivalentTo(new[] { VRButton.A }));
        Assert.That(press.CursorHand, Is.EqualTo(PointerHand.Right));

        var held = pointer.Update(Pressed(VRButton.RightTrigger), null, null);
        Assert.That(held.Down, Is.Empty);
        Assert.That(held.GamepadButtons, Is.Empty);

        var release = pointer.Update(Pressed(), null, OnPanel);
        Assert.That(release.Up, Is.EqualTo(new[] { PointerClick.Left }));
    }

    [Test]
    public void TriggerPressedAwayFromTheUIStaysAGamepadTrigger()
    {
        var pointer = new ControllerPointer();
        var press = pointer.Update(Pressed(VRButton.LeftTrigger), null, null);
        Assert.That(press.GamepadButtons, Is.EquivalentTo(new[] { VRButton.LeftTrigger }));

        var aimed = pointer.Update(Pressed(VRButton.LeftTrigger), OnPanel, null);
        Assert.That(aimed.Down, Is.Empty);
        Assert.That(aimed.GamepadButtons, Is.EquivalentTo(new[] { VRButton.LeftTrigger }));

        var release = pointer.Update(Pressed(), OnPanel, null);
        Assert.That(release.Up, Is.Empty);
    }

    [Test]
    public void GripRightClicks()
    {
        var pointer = new ControllerPointer();
        Assert.That(pointer.Update(Pressed(VRButton.LeftGrip), OnPanel, null).Down, Is.EqualTo(new[] { PointerClick.Right }));
        Assert.That(pointer.Update(Pressed(), OnPanel, null).Up, Is.EqualTo(new[] { PointerClick.Right }));
    }

    [Test]
    public void BothTriggersMakeOneClick()
    {
        var pointer = new ControllerPointer();
        Assert.That(pointer.Update(Pressed(VRButton.LeftTrigger), OnPanel, OnPanel).Down, Has.Count.EqualTo(1));
        Assert.That(pointer.Update(Pressed(VRButton.LeftTrigger, VRButton.RightTrigger), OnPanel, OnPanel).Down, Is.Empty);
        Assert.That(pointer.Update(Pressed(VRButton.RightTrigger), OnPanel, OnPanel).Up, Is.Empty);
        Assert.That(pointer.Update(Pressed(), OnPanel, OnPanel).Up, Has.Count.EqualTo(1));
    }

    [Test]
    public void AStillControllerDoesNotHoldTheCursor()
    {
        var pointer = new ControllerPointer();
        Assert.That(pointer.Update(Pressed(), null, OnPanel).CursorHand, Is.EqualTo(PointerHand.Right));
        var still = pointer.Update(Pressed(), null, OnPanel + new Vector2D<float>(ControllerPointer.MoveThreshold / 2, 0));
        Assert.That(still.CursorHand, Is.Null);
        Assert.That(still.LaserHand, Is.EqualTo(PointerHand.Right));

        var moved = pointer.Update(Pressed(), null, OnPanel + new Vector2D<float>(ControllerPointer.MoveThreshold * 2, 0));
        Assert.That(moved.CursorHand, Is.EqualTo(PointerHand.Right));
    }

    [Test]
    public void NoLaserWhenNotPointingAtTheUI()
    {
        var pointer = new ControllerPointer();
        var frame = pointer.Update(Pressed(VRButton.B), null, null);
        Assert.That(frame.LaserHand, Is.Null);
        Assert.That(frame.CursorHand, Is.Null);
        Assert.That(frame.GamepadButtons, Is.EquivalentTo(new[] { VRButton.B }));
    }
}