using Silk.NET.Maths;

namespace FfxivVR.Tests;

public class CameraShakeTests
{
    private static readonly Vector3D<float> CameraPosition = new(10, 2, -5);
    private static Matrix4X4<float> View(Matrix4X4<float> world)
    {
        Matrix4X4.Invert(world, out var view);
        return view;
    }

    private static Matrix4X4<float> CameraWorld(float yaw, float pitch, Matrix4X4<float> roll, Vector3D<float> position)
    {
        return roll * Matrix4X4.CreateRotationX(pitch) * Matrix4X4.CreateRotationY(yaw) * Matrix4X4.CreateTranslation(position);
    }

    private static Matrix4X4<float> RolledView(float rollDegrees, float yawDegrees = 0)
    {
        var roll = Matrix4X4.CreateFromQuaternion(Quaternion<float>.CreateFromAxisAngle(-Vector3D<float>.UnitZ, float.DegreesToRadians(rollDegrees)));
        return View(CameraWorld(float.DegreesToRadians(yawDegrees), float.DegreesToRadians(-20), roll, new Vector3D<float>(10, 2, -5)));
    }

    [Test]
    public void ReadsBackTheRollTheVRViewApplies()
    {
        foreach (var yaw in new[] { 0f, 35f, -120f })
        {
            Assert.That(float.RadiansToDegrees(CameraShake.ExtractRoll(RolledView(7, yaw))), Is.EqualTo(7).Within(0.01f));
            Assert.That(float.RadiansToDegrees(CameraShake.ExtractRoll(RolledView(-3, yaw))), Is.EqualTo(-3).Within(0.01f));
        }
        Assert.That(CameraShake.ExtractRoll(RolledView(0)), Is.EqualTo(0).Within(1E-5f));
    }

    [Test]
    public void SteadyOffsetsFadeOut()
    {
        var shake = new CameraShake();
        var anchor = new Vector3D<float>(100, 5, 100);
        for (int frame = 0; frame <= 90; frame++)
        {
            var settled = frame > 5;
            shake.Update(anchor + new Vector3D<float>(0, settled ? 0.3f : 0, 0), anchor, CameraPosition, RolledView(settled ? 4 : 0), frame / 90.0);
            if (frame == 6)
            {
                Assert.That(shake.Offset.Length, Is.GreaterThan(0.2f));
                Assert.That(float.RadiansToDegrees(shake.Roll), Is.GreaterThan(3));
            }
        }
        Assert.That(shake.Offset.Length, Is.LessThan(1E-3f));
        Assert.That(MathF.Abs(shake.Roll), Is.LessThan(1E-3f));
    }

    [Test]
    public void FastJitterPassesThrough()
    {
        var shake = new CameraShake();
        var anchor = new Vector3D<float>(0, 1.6f, 0);
        var peak = 0f;
        var peakRoll = 0f;
        for (int frame = 0; frame < 180; frame++)
        {
            var time = frame / 90.0;
            var wave = MathF.Sin(2 * MathF.PI * 12 * (float)time);
            shake.Update(anchor + new Vector3D<float>(0, 0.1f * wave, 0), anchor, CameraPosition, RolledView(2 * wave), time);
            if (frame > 90)
            {
                peak = MathF.Max(peak, MathF.Abs(shake.Offset.Y));
                peakRoll = MathF.Max(peakRoll, MathF.Abs(float.RadiansToDegrees(shake.Roll)));
            }
        }
        Assert.That(peak, Is.GreaterThan(0.07f));
        Assert.That(peakRoll, Is.GreaterThan(1.4f));
    }

    [Test]
    public void TeleportsDoNotJolt()
    {
        var shake = new CameraShake();
        var view = RolledView(0);
        shake.Update(new Vector3D<float>(0, 1.6f, 0), Vector3D<float>.Zero, CameraPosition, view, 0);
        shake.Update(new Vector3D<float>(0, 1.6f, 0), Vector3D<float>.Zero, CameraPosition, view, 0.011);
        shake.Update(new Vector3D<float>(50, 1.6f, 0), Vector3D<float>.Zero, CameraPosition, view, 0.022);
        Assert.That(shake.Offset.Length, Is.LessThan(1E-5f));
    }

    [Test]
    public void BadFramesDoNotStick()
    {
        var shake = new CameraShake();
        var anchor = new Vector3D<float>(0, 1.6f, 0);
        var view = RolledView(0);
        var nan = new Matrix4X4<float>(float.NaN, 0, 0, 0, 0, float.NaN, 0, 0, 0, 0, float.NaN, 0, 0, 0, 0, 1);
        shake.Update(anchor, anchor, CameraPosition, view, 0);
        shake.Update(anchor + new Vector3D<float>(float.NaN, 0, 0), anchor, CameraPosition, nan, 0.011);
        Assert.That(shake.Offset, Is.EqualTo(Vector3D<float>.Zero));
        Assert.That(shake.Roll, Is.EqualTo(0));
        for (int frame = 2; frame < 10; frame++)
        {
            shake.Update(anchor, anchor, CameraPosition, view, frame * 0.011);
            Assert.That(float.IsFinite(shake.Offset.X) && float.IsFinite(shake.Roll), Is.True);
        }
        Assert.That(CameraShake.ExtractRoll(nan), Is.EqualTo(0));
        Assert.That(CameraShake.ExtractRoll(default), Is.EqualTo(0));
    }

    [Test]
    public void OffsetIsClamped()
    {
        var shake = new CameraShake();
        var view = RolledView(0);
        shake.Update(Vector3D<float>.Zero, Vector3D<float>.Zero, CameraPosition, view, 0);
        shake.Update(new Vector3D<float>(0, 0.9f, 0), Vector3D<float>.Zero, CameraPosition, view, 0.011);
        Assert.That(shake.Offset.Length, Is.LessThanOrEqualTo(CameraShake.MaxOffset + 1E-5f));
    }

    [Test]
    public void StrengthScalesUpToTheLimits()
    {
        var shake = new CameraShake();
        var view = RolledView(0);
        shake.Update(Vector3D<float>.Zero, Vector3D<float>.Zero, CameraPosition, view, 0);
        shake.Update(new Vector3D<float>(0, 0.05f, 0), Vector3D<float>.Zero, CameraPosition, RolledView(1), 0.011);
        Assert.That(shake.ScaledOffset(10).Length, Is.EqualTo(shake.Offset.Length * 10).Within(1E-5f));
        Assert.That(shake.ScaledRoll(10), Is.EqualTo(shake.Roll * 10).Within(1E-5f));

        shake.Update(new Vector3D<float>(0, 0.45f, 0), Vector3D<float>.Zero, CameraPosition, RolledView(9), 0.022);
        Assert.That(shake.ScaledOffset(10).Length, Is.LessThanOrEqualTo(CameraShake.MaxScaledOffset + 1E-5f));
        Assert.That(MathF.Abs(shake.ScaledRoll(10)), Is.LessThanOrEqualTo(CameraShake.MaxScaledAngle + 1E-5f));
        Assert.That(shake.ScaledOffset(0), Is.EqualTo(Vector3D<float>.Zero));
    }

    private record ShakePeaks(float LookAt, float Matrix, float Pitch, float Yaw, float Roll, float Leftover);

    private static ShakePeaks RunTest(ShakeSource source, bool freshViews = true)
    {
        var shake = new CameraShake();
        var position = new Vector3D<float>(20, 3, -8);
        var lookAt = LookAtFor(position, 30, -15);
        var view = QuakedView(position, 30, -15, Vector3D<float>.Zero);
        shake.Update(lookAt, lookAt, position, view, 0);
        shake.StartTest(source, 0);
        float lookAtPeak = 0, matrixPeak = 0, pitchPeak = 0, yawPeak = 0, rollPeak = 0;
        var time = 0.0;
        while (time < ShakeTest.Seconds)
        {
            time += 1 / 90.0;
            shake.Update(lookAt, lookAt, position, freshViews ? view : null, time);
            lookAtPeak = MathF.Max(lookAtPeak, shake.Offset.Length);
            matrixPeak = MathF.Max(matrixPeak, shake.EyeOffset.Length);
            pitchPeak = MathF.Max(pitchPeak, MathF.Abs(float.RadiansToDegrees(shake.Pitch)));
            yawPeak = MathF.Max(yawPeak, MathF.Abs(float.RadiansToDegrees(shake.Yaw)));
            rollPeak = MathF.Max(rollPeak, MathF.Abs(float.RadiansToDegrees(shake.Roll)));
        }
        for (int frame = 0; frame < 45; frame++)
        {
            time += 1 / 90.0;
            shake.Update(lookAt, lookAt, position, freshViews ? view : null, time);
        }
        Assert.That(shake.IsTesting, Is.False);
        var leftover = MathF.Max(MathF.Max(shake.Offset.Length, shake.EyeOffset.Length), float.RadiansToDegrees(MathF.Max(MathF.Abs(shake.Pitch), MathF.Max(MathF.Abs(shake.Yaw), MathF.Abs(shake.Roll)))));
        return new ShakePeaks(lookAtPeak, matrixPeak, pitchPeak, yawPeak, rollPeak, leftover);
    }

    [Test]
    public void LookAtTestShakesOnlyTheLookAt()
    {
        var peaks = RunTest(ShakeSource.LookAt);
        Assert.That(peaks.LookAt, Is.GreaterThan(0.03f));
        Assert.That(peaks.Matrix, Is.LessThan(1E-3f));
        Assert.That(MathF.Max(peaks.Pitch, MathF.Max(peaks.Yaw, peaks.Roll)), Is.LessThan(0.05f));
        Assert.That(peaks.Leftover, Is.LessThan(1E-3f));
    }

    [Test]
    public void CameraPositionTestShakesTheMatrixOffset()
    {
        var peaks = RunTest(ShakeSource.CameraPosition);
        Assert.That(peaks.Matrix, Is.GreaterThan(0.03f));
        Assert.That(peaks.LookAt, Is.LessThan(1E-4f));
        Assert.That(MathF.Max(peaks.Pitch, MathF.Max(peaks.Yaw, peaks.Roll)), Is.LessThan(0.05f));
        Assert.That(peaks.Leftover, Is.LessThan(1E-3f));
    }

    [Test]
    public void RotationTestShakesPitchAndYaw()
    {
        var peaks = RunTest(ShakeSource.CameraRotation);
        Assert.That(peaks.Pitch, Is.GreaterThan(0.5f));
        Assert.That(peaks.Yaw, Is.GreaterThan(0.3f));
        Assert.That(peaks.Roll, Is.LessThan(0.2f));
        Assert.That(MathF.Max(peaks.LookAt, peaks.Matrix), Is.LessThan(1E-3f));
        Assert.That(peaks.Leftover, Is.LessThan(1E-3f));
    }

    [Test]
    public void RollTestShakesOnlyTheRoll()
    {
        var peaks = RunTest(ShakeSource.CameraRoll);
        Assert.That(peaks.Roll, Is.GreaterThan(0.5f));
        Assert.That(MathF.Max(peaks.Pitch, peaks.Yaw), Is.LessThan(0.05f));
        Assert.That(MathF.Max(peaks.LookAt, peaks.Matrix), Is.LessThan(1E-3f));
        Assert.That(peaks.Leftover, Is.LessThan(1E-3f));
    }

    [Test]
    public void QuakeTestWorksWhenTheGameSkipsRebuildingTheView()
    {
        var peaks = RunTest(ShakeSource.Quake, freshViews: false);
        Assert.That(peaks.Matrix, Is.GreaterThan(0.03f));
        Assert.That(peaks.Pitch, Is.GreaterThan(0.5f));
        Assert.That(peaks.Roll, Is.GreaterThan(0.5f));
        Assert.That(peaks.Leftover, Is.LessThan(1E-3f));
    }

    private static Matrix4X4<float> Aim(float yawDegrees, float pitchDegrees)
    {
        return Matrix4X4.CreateRotationX(float.DegreesToRadians(pitchDegrees)) * Matrix4X4.CreateRotationY(float.DegreesToRadians(yawDegrees));
    }

    private static Vector3D<float> LookAtFor(Vector3D<float> position, float yawDegrees, float pitchDegrees)
    {
        return position + Vector3D.TransformNormal(-Vector3D<float>.UnitZ, Aim(yawDegrees, pitchDegrees)) * 5;
    }

    private static Matrix4X4<float> QuakedView(Vector3D<float> position, float yawDegrees, float pitchDegrees, Vector3D<float> eyeShift)
    {
        return View(Aim(yawDegrees, pitchDegrees) * Matrix4X4.CreateTranslation(position + eyeShift));
    }

    [Test]
    public void ReadsTheTiltBetweenTheMatrixAndTheLookAt()
    {
        var position = new Vector3D<float>(3, 4, 5);
        var lookAt = LookAtFor(position, 30, -15);
        var (pitch, yaw) = CameraShake.ExtractTilt(QuakedView(position, 30, -13, Vector3D<float>.Zero), position, lookAt);
        Assert.That(float.RadiansToDegrees(pitch), Is.EqualTo(2).Within(0.01f));
        Assert.That(float.RadiansToDegrees(yaw), Is.EqualTo(0).Within(0.01f));

        var levelLookAt = LookAtFor(position, 30, 0);
        var (_, turned) = CameraShake.ExtractTilt(QuakedView(position, 33, 0, Vector3D<float>.Zero), position, levelLookAt);
        Assert.That(float.RadiansToDegrees(turned), Is.EqualTo(-3).Within(0.01f));
        Assert.That(CameraShake.ExtractEye(QuakedView(position, 33, 0, new Vector3D<float>(0, 0.2f, 0))), Is.EqualTo(position + new Vector3D<float>(0, 0.2f, 0)).Using<Vector3D<float>>((a, b) => (a - b).Length < 1E-4f));
    }

    [Test]
    public void QuakeInTheViewMatrixPassesThrough()
    {
        var shake = new CameraShake();
        var position = new Vector3D<float>(20, 3, -8);
        var lookAt = LookAtFor(position, 30, -15);
        var anchor = lookAt;
        var peakEye = 0f;
        var peakPitch = 0f;
        var peakLookAt = 0f;
        for (int frame = 0; frame < 180; frame++)
        {
            var time = frame / 90.0;
            var wave = MathF.Sin(2 * MathF.PI * 12 * (float)time);
            shake.Update(lookAt, anchor, position, QuakedView(position, 30, -15 + 2 * wave, new Vector3D<float>(0, 0.1f * wave, 0)), time);
            if (frame > 90)
            {
                peakEye = MathF.Max(peakEye, MathF.Abs(shake.EyeOffset.Y));
                peakPitch = MathF.Max(peakPitch, MathF.Abs(float.RadiansToDegrees(shake.Pitch)));
                peakLookAt = MathF.Max(peakLookAt, shake.Offset.Length);
            }
        }
        Assert.That(peakEye, Is.GreaterThan(0.07f));
        Assert.That(peakPitch, Is.GreaterThan(1.4f));
        Assert.That(peakLookAt, Is.LessThan(1E-4f));
    }

    [Test]
    public void SkippedViewsLetTheShakeSettle()
    {
        var shake = new CameraShake();
        var position = new Vector3D<float>(20, 3, -8);
        var lookAt = LookAtFor(position, 0, 0);
        shake.Update(lookAt, lookAt, position, RolledView(0), 0);
        shake.Update(lookAt, lookAt, position, RolledView(6), 0.011);
        Assert.That(float.RadiansToDegrees(shake.Roll), Is.GreaterThan(4));
        var time = 0.011;
        for (int frame = 0; frame < 60; frame++)
        {
            time += 0.011;
            shake.Update(lookAt, lookAt, position, null, time);
        }
        Assert.That(MathF.Abs(shake.Roll), Is.LessThan(1E-3f));
        Assert.That(shake.EyeOffset.Length, Is.LessThan(1E-3f));
    }
}