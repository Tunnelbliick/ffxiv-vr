using Silk.NET.Maths;
using System;

namespace FfxivVR;

public class CameraShake
{
    public const float TimeConstant = 0.06f;
    public const float MaxOffset = 0.5f;
    public static readonly float MaxRoll = float.DegreesToRadians(10);
    private const float ResetDistance = 1f;
    private const double MaxStep = 0.25;
    private const float PeakDecaySeconds = 2f;

    private Vector3D<float> offsetAverage;
    private float rollAverage;
    private double? lastUpdate;

    public Vector3D<float> Offset { get; private set; }
    public float Roll { get; private set; }
    public Vector3D<float> RawOffset { get; private set; }
    public float RawRoll { get; private set; }
    public float PeakOffset { get; private set; }
    public float PeakRoll { get; private set; }

    public void Update(Vector3D<float> lookAt, Vector3D<float> anchor, Matrix4X4<float> gameView, double now)
    {
        var dt = lastUpdate is double last ? now - last : double.PositiveInfinity;
        lastUpdate = now;
        RawOffset = lookAt - anchor;
        RawRoll = ExtractRoll(gameView);
        if (dt > MaxStep || dt < 0 || (RawOffset - offsetAverage).Length > ResetDistance)
        {
            offsetAverage = RawOffset;
            rollAverage = RawRoll;
        }
        else
        {
            var amount = 1 - MathF.Exp(-(float)dt / TimeConstant);
            offsetAverage += (RawOffset - offsetAverage) * amount;
            rollAverage += MathFactory.AcuteAngleBetween(rollAverage, RawRoll) * amount;
        }
        var offset = RawOffset - offsetAverage;
        Offset = offset.Length > MaxOffset ? offset * (MaxOffset / offset.Length) : offset;
        Roll = Math.Clamp(MathFactory.AcuteAngleBetween(rollAverage, RawRoll), -MaxRoll, MaxRoll);

        var decay = float.IsFinite((float)dt) ? MathF.Exp(-(float)dt / PeakDecaySeconds) : 0;
        PeakOffset = MathF.Max(PeakOffset * decay, Offset.Length);
        PeakRoll = MathF.Max(PeakRoll * decay, MathF.Abs(Roll));
    }

    public static float ExtractRoll(Matrix4X4<float> view)
    {
        if (!Matrix4X4.Invert(view, out var world))
        {
            return 0;
        }
        var forward = -new Vector3D<float>(world.M31, world.M32, world.M33);
        var up = new Vector3D<float>(world.M21, world.M22, world.M23);
        if (forward.Length < 1E-6f || up.Length < 1E-6f)
        {
            return 0;
        }
        forward = Vector3D.Normalize(forward);
        var levelRight = Vector3D.Cross(forward, Vector3D<float>.UnitY);
        if (levelRight.Length < 1E-3f)
        {
            return 0;
        }
        levelRight = Vector3D.Normalize(levelRight);
        var levelUp = Vector3D.Cross(levelRight, forward);
        up = Vector3D.Normalize(up - forward * Vector3D.Dot(up, forward));
        return MathF.Atan2(Vector3D.Dot(Vector3D.Cross(levelUp, up), forward), Vector3D.Dot(levelUp, up));
    }

    public Matrix4X4<float> RollMatrix(float strength)
    {
        return Matrix4X4.CreateFromQuaternion(Quaternion<float>.CreateFromAxisAngle(-Vector3D<float>.UnitZ, Roll * strength));
    }
}