using Silk.NET.Maths;

namespace FfxivVR.Tests;

public class CameraShakeTests
{
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
            shake.Update(anchor + new Vector3D<float>(0, settled ? 0.3f : 0, 0), anchor, RolledView(settled ? 4 : 0), frame / 90.0);
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
            shake.Update(anchor + new Vector3D<float>(0, 0.1f * wave, 0), anchor, RolledView(2 * wave), time);
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
        shake.Update(new Vector3D<float>(0, 1.6f, 0), Vector3D<float>.Zero, view, 0);
        shake.Update(new Vector3D<float>(0, 1.6f, 0), Vector3D<float>.Zero, view, 0.011);
        shake.Update(new Vector3D<float>(50, 1.6f, 0), Vector3D<float>.Zero, view, 0.022);
        Assert.That(shake.Offset.Length, Is.LessThan(1E-5f));
    }

    [Test]
    public void BadFramesDoNotStick()
    {
        var shake = new CameraShake();
        var anchor = new Vector3D<float>(0, 1.6f, 0);
        var view = RolledView(0);
        var nan = new Matrix4X4<float>(float.NaN, 0, 0, 0, 0, float.NaN, 0, 0, 0, 0, float.NaN, 0, 0, 0, 0, 1);
        shake.Update(anchor, anchor, view, 0);
        shake.Update(anchor + new Vector3D<float>(float.NaN, 0, 0), anchor, nan, 0.011);
        Assert.That(shake.Offset, Is.EqualTo(Vector3D<float>.Zero));
        Assert.That(shake.Roll, Is.EqualTo(0));
        for (int frame = 2; frame < 10; frame++)
        {
            shake.Update(anchor, anchor, view, frame * 0.011);
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
        shake.Update(Vector3D<float>.Zero, Vector3D<float>.Zero, view, 0);
        shake.Update(new Vector3D<float>(0, 0.9f, 0), Vector3D<float>.Zero, view, 0.011);
        Assert.That(shake.Offset.Length, Is.LessThanOrEqualTo(CameraShake.MaxOffset + 1E-5f));
    }

    [Test]
    public void StrengthScalesUpToTheLimits()
    {
        var shake = new CameraShake();
        var view = RolledView(0);
        shake.Update(Vector3D<float>.Zero, Vector3D<float>.Zero, view, 0);
        shake.Update(new Vector3D<float>(0, 0.05f, 0), Vector3D<float>.Zero, RolledView(1), 0.011);
        Assert.That(shake.ScaledOffset(10).Length, Is.EqualTo(shake.Offset.Length * 10).Within(1E-5f));
        Assert.That(shake.ScaledRoll(10), Is.EqualTo(shake.Roll * 10).Within(1E-5f));

        shake.Update(new Vector3D<float>(0, 0.45f, 0), Vector3D<float>.Zero, RolledView(9), 0.022);
        Assert.That(shake.ScaledOffset(10).Length, Is.LessThanOrEqualTo(CameraShake.MaxScaledOffset + 1E-5f));
        Assert.That(MathF.Abs(shake.ScaledRoll(10)), Is.LessThanOrEqualTo(CameraShake.MaxScaledRoll + 1E-5f));
        Assert.That(shake.ScaledOffset(0), Is.EqualTo(Vector3D<float>.Zero));
    }

    [Test]
    public void TestShakePlaysOnceThenStops()
    {
        var shake = new CameraShake();
        var anchor = new Vector3D<float>(100, 5, 100);
        var view = RolledView(0);
        shake.Update(anchor, anchor, view, 0);
        shake.StartTest(0);
        var peak = 0f;
        var time = 0.0;
        while (time < CameraShake.TestSeconds)
        {
            time += 0.011;
            shake.Update(anchor, anchor, view, time);
            peak = MathF.Max(peak, shake.Offset.Length);
        }
        Assert.That(peak, Is.GreaterThan(0.02f));
        shake.Update(anchor, anchor, view, time + 0.011);
        Assert.That(shake.Offset.Length, Is.EqualTo(0).Within(1E-5f));
        Assert.That(shake.Roll, Is.EqualTo(0).Within(1E-5f));
    }
}