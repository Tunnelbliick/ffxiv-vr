using Silk.NET.Maths;

namespace FfxivVR.Tests;

public class BodyYawTests
{
    private static readonly Vector3D<float> VRForward = -Vector3D<float>.UnitZ;

    private static Quaternion<float> Head(float yawDegrees, float pitchDegrees = 0)
    {
        return Quaternion<float>.CreateFromYawPitchRoll(float.DegreesToRadians(yawDegrees), float.DegreesToRadians(pitchDegrees), 0);
    }

    private static float Degrees(float radians)
    {
        return float.RadiansToDegrees(MathFactory.AcuteAngleBetween(0, radians));
    }

    [Test]
    public void FacingYawMatchesTheYRotationConvention()
    {
        foreach (var degrees in new[] { 0f, 30f, 90f, 135f, -60f, 180f })
        {
            var forward = Vector3D.Transform(VRForward, Head(degrees, 20));
            var flat = Vector3D.Normalize(new Vector3D<float>(forward.X, 0, forward.Z));
            var rotated = Vector3D.Transform(VRForward, MathFactory.YRotation(BodyYaw.FacingYaw(forward)));
            Assert.That(Vector3D.Distance(rotated, flat), Is.LessThan(1E-4f));
            var heading = BodyYaw.HeadingYaw(Head(degrees, 20))!.Value;
            Assert.That(MathF.Abs(MathFactory.AcuteAngleBetween(heading, float.DegreesToRadians(degrees))), Is.LessThan(1E-4f));
        }
        Assert.That(BodyYaw.HeadingYaw(Head(10, 90)), Is.Null);
    }

    [Test]
    public void ThirdPersonMapsYourBodyForwardToTheCharacterForward()
    {
        foreach (var characterDegrees in new[] { 0f, 70f, 180f, -110f })
        {
            foreach (var bodyDegrees in new[] { 0f, 40f, -150f })
            {
                var character = MathFactory.YRotation(float.DegreesToRadians(characterDegrees));
                var body = float.DegreesToRadians(bodyDegrees);
                var trackingYaw = BodyYaw.ThirdPersonTrackingYaw(character, body);
                var skeletonRotation = MathFactory.YRotation(trackingYaw) / character;

                var bodyForward = Vector3D.Transform(VRForward, MathFactory.YRotation(body));
                Assert.That(Vector3D.Distance(Vector3D.Transform(bodyForward, skeletonRotation), Vector3D<float>.UnitZ), Is.LessThan(1E-4f));

                var bodyRight = Vector3D.Transform(Vector3D<float>.UnitX, MathFactory.YRotation(body));
                var modelRight = Vector3D.Transform(bodyRight, skeletonRotation);
                Assert.That(Vector3D.Distance(modelRight, Vector3D.Transform(Vector3D<float>.UnitX, MathFactory.YRotation(MathF.PI))), Is.LessThan(1E-4f));
            }
        }
    }

    [Test]
    public void StandingBodyLagsSmallHeadTurnsAndFollowsLargeOnes()
    {
        var yaw = new BodyYaw();
        Assert.That(Degrees(yaw.Update(Head(0), false, 0)), Is.EqualTo(0).Within(1E-3f));

        var afterGlance = yaw.Update(Head(30), false, 0.011);
        Assert.That(Degrees(afterGlance), Is.LessThan(1));

        var afterTurn = yaw.Update(Head(80), false, 0.022);
        Assert.That(Degrees(afterTurn), Is.EqualTo(80 - float.RadiansToDegrees(BodyYaw.MaxHeadTurn)).Within(0.5f));

        var time = 0.022;
        var latest = afterTurn;
        for (int frame = 0; frame < 900; frame++)
        {
            time += 0.011;
            latest = yaw.Update(Head(80), false, time);
        }
        Assert.That(Degrees(latest), Is.EqualTo(80).Within(1));
    }

    [Test]
    public void SeatedBodyKeepsTheRecenterForward()
    {
        var yaw = new BodyYaw();
        for (int frame = 0; frame < 300; frame++)
        {
            Assert.That(yaw.Update(Head(70), true, frame * 0.011), Is.EqualTo(0));
        }
    }

    [Test]
    public void RecenterJumpsResetTheBody()
    {
        var yaw = new BodyYaw();
        yaw.Update(Head(150), false, 0);
        var reset = yaw.Update(Head(0), false, 0.011);
        Assert.That(Degrees(reset), Is.EqualTo(0).Within(1E-3f));
    }

    [Test]
    public void ResetFollowsTheNextHeadingAtOnce()
    {
        var yaw = new BodyYaw();
        yaw.Update(Head(40), false, 0);
        yaw.Reset();
        Assert.That(Degrees(yaw.Update(Head(-30), false, 0.011)), Is.EqualTo(-30).Within(1E-3f));
    }

    [Test]
    public void ResetCameraLooksWhereTheCharacterFaces()
    {
        foreach (var degrees in new[] { -180f, -120f, -45f, 0f, 30f, 90f, 179f })
        {
            var rotation = float.DegreesToRadians(degrees);
            var direction = BodyYaw.CameraDirectionBehind(rotation);
            var facing = BodyYaw.FacingYaw(new Vector3D<float>(MathF.Sin(rotation), 0, MathF.Cos(rotation)));
            Assert.That(MathFactory.AcuteAngleBetween(direction, facing), Is.EqualTo(0).Within(1E-4f));
            Assert.That(MathF.Abs(direction), Is.LessThanOrEqualTo(MathF.PI + 1E-5f));
        }
    }
}