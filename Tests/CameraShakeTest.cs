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
    public void OffsetIsClamped()
    {
        var shake = new CameraShake();
        var view = RolledView(0);
        shake.Update(Vector3D<float>.Zero, Vector3D<float>.Zero, view, 0);
        shake.Update(new Vector3D<float>(0, 0.9f, 0), Vector3D<float>.Zero, view, 0.011);
        Assert.That(shake.Offset.Length, Is.LessThanOrEqualTo(CameraShake.MaxOffset + 1E-5f));
    }
}