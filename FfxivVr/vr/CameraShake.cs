using Silk.NET.Maths;
using System;

namespace FfxivVR;

public class CameraShake
{
    public const float TimeConstant = 0.06f;
    public const float MaxOffset = 0.5f;
    public static readonly float MaxAngle = float.DegreesToRadians(10);
    private const float ResetDistance = 1f;
    private static readonly float ResetAngle = float.DegreesToRadians(20);
    private const double MaxStep = 0.25;
    private const float PeakDecaySeconds = 2f;
    public const float MaxScaledOffset = 2f;
    public static readonly float MaxScaledAngle = float.DegreesToRadians(30);

    private double? testStart;
    private ShakeSource testSource;
    private Matrix4X4<float>? lastView;
    private double? lastUpdate;
    private Vector3D<float> offsetAverage;
    private Vector3D<float> eyeAverage;
    private Vector3D<float> angleAverage;

    public Vector3D<float> Offset { get; private set; }
    public Vector3D<float> EyeOffset { get; private set; }
    public float Pitch { get; private set; }
    public float Yaw { get; private set; }
    public float Roll { get; private set; }
    public Vector3D<float> RawOffset { get; private set; }
    public Vector3D<float> RawEyeOffset { get; private set; }
    public float RawPitch { get; private set; }
    public float RawYaw { get; private set; }
    public float RawRoll { get; private set; }
    public float PeakOffset { get; private set; }
    public float PeakAngle { get; private set; }

    public void Update(Vector3D<float> lookAt, Vector3D<float> anchor, Vector3D<float> cameraPosition, Matrix4X4<float>? gameView, double now)
    {
        if (gameView is { } fresh)
        {
            lastView = fresh;
        }
        if (TestTime(now) is float t)
        {
            var shakenLookAt = ShakeTest.ShakeLookAt(lookAt, testSource, t);
            if ((gameView ?? lastView) is { } baseView)
            {
                gameView = ShakeTest.ShakeView(baseView, cameraPosition, lookAt, shakenLookAt, testSource, t);
            }
            lookAt = shakenLookAt;
        }
        RawOffset = lookAt - anchor;
        if (gameView is { } view)
        {
            RawEyeOffset = ExtractEye(view) - cameraPosition;
            (RawPitch, RawYaw) = ExtractTilt(view, cameraPosition, lookAt);
            RawRoll = ExtractRoll(view);
        }
        var rawAngles = new Vector3D<float>(RawPitch, RawYaw, RawRoll);
        if (!IsFinite(RawOffset) || !IsFinite(RawEyeOffset) || !IsFinite(rawAngles))
        {
            lastUpdate = null;
            RawEyeOffset = Vector3D<float>.Zero;
            RawPitch = RawYaw = RawRoll = 0;
            Offset = EyeOffset = Vector3D<float>.Zero;
            Pitch = Yaw = Roll = 0;
            return;
        }
        var dt = lastUpdate is double last ? now - last : double.PositiveInfinity;
        lastUpdate = now;
        var restart = dt > MaxStep || dt < 0;
        var amount = restart ? 1 : 1 - MathF.Exp(-(float)dt / TimeConstant);

        offsetAverage = Follow(offsetAverage, RawOffset, amount, restart);
        eyeAverage = Follow(eyeAverage, RawEyeOffset, amount, restart);
        var angleChange = new Vector3D<float>(
            MathFactory.AcuteAngleBetween(angleAverage.X, rawAngles.X),
            MathFactory.AcuteAngleBetween(angleAverage.Y, rawAngles.Y),
            MathFactory.AcuteAngleBetween(angleAverage.Z, rawAngles.Z));
        if (restart || !IsFinite(angleAverage) || MathF.Max(MathF.Abs(angleChange.X), MathF.Max(MathF.Abs(angleChange.Y), MathF.Abs(angleChange.Z))) > ResetAngle)
        {
            angleAverage = rawAngles;
            angleChange = Vector3D<float>.Zero;
        }
        else
        {
            angleAverage += angleChange * amount;
            angleChange *= 1 - amount;
        }

        Offset = ClampLength(RawOffset - offsetAverage, MaxOffset);
        EyeOffset = ClampLength(RawEyeOffset - eyeAverage, MaxOffset);
        Pitch = Math.Clamp(angleChange.X, -MaxAngle, MaxAngle);
        Yaw = Math.Clamp(angleChange.Y, -MaxAngle, MaxAngle);
        Roll = Math.Clamp(angleChange.Z, -MaxAngle, MaxAngle);

        var decay = float.IsFinite((float)dt) ? MathF.Exp(-(float)dt / PeakDecaySeconds) : 0;
        PeakOffset = MathF.Max(PeakOffset * decay, MathF.Max(Offset.Length, EyeOffset.Length));
        PeakAngle = MathF.Max(PeakAngle * decay, MathF.Max(MathF.Abs(Pitch), MathF.Max(MathF.Abs(Yaw), MathF.Abs(Roll))));
    }

    private static Vector3D<float> Follow(Vector3D<float> average, Vector3D<float> raw, float amount, bool restart)
    {
        if (restart || !IsFinite(average) || (raw - average).Length > ResetDistance)
        {
            return raw;
        }
        return average + (raw - average) * amount;
    }

    private static Vector3D<float> ClampLength(Vector3D<float> vector, float max)
    {
        return vector.Length > max ? vector * (max / vector.Length) : vector;
    }

    public static Vector3D<float> ExtractEye(Matrix4X4<float> view)
    {
        if (!Matrix4X4.Invert(view, out var world))
        {
            return new Vector3D<float>(float.NaN);
        }
        return new Vector3D<float>(world.M41, world.M42, world.M43);
    }

    public static (float Pitch, float Yaw) ExtractTilt(Matrix4X4<float> view, Vector3D<float> cameraPosition, Vector3D<float> lookAt)
    {
        if (!Matrix4X4.Invert(view, out var world))
        {
            return (0, 0);
        }
        var forward = -new Vector3D<float>(world.M31, world.M32, world.M33);
        var target = lookAt - cameraPosition;
        if (forward.Length < 1E-6f || target.Length < 1E-6f)
        {
            return (0, 0);
        }
        forward = Vector3D.Normalize(forward);
        target = Vector3D.Normalize(target);
        var right = Vector3D.Cross(target, Vector3D<float>.UnitY);
        if (right.Length < 1E-3f)
        {
            return (0, 0);
        }
        right = Vector3D.Normalize(right);
        var up = Vector3D.Cross(right, target);
        var along = Vector3D.Dot(forward, target);
        var pitch = MathF.Atan2(Vector3D.Dot(forward, up), along);
        var yaw = MathF.Atan2(Vector3D.Dot(forward, right), along);
        return float.IsFinite(pitch) && float.IsFinite(yaw) ? (pitch, yaw) : (0, 0);
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
        up -= forward * Vector3D.Dot(up, forward);
        if (up.Length < 1E-6f)
        {
            return 0;
        }
        up = Vector3D.Normalize(up);
        var roll = MathF.Atan2(Vector3D.Dot(Vector3D.Cross(levelUp, up), forward), Vector3D.Dot(levelUp, up));
        return float.IsFinite(roll) ? roll : 0;
    }

    private static bool IsFinite(Vector3D<float> vector)
    {
        return float.IsFinite(vector.X) && float.IsFinite(vector.Y) && float.IsFinite(vector.Z);
    }

    public void StartTest(ShakeSource source, double now)
    {
        testSource = source;
        testStart = now;
    }

    public bool IsTesting => testStart != null;

    private float? TestTime(double now)
    {
        if (testStart is not double start)
        {
            return null;
        }
        var t = (float)(now - start);
        if (t >= 0 && t < ShakeTest.Seconds)
        {
            return t;
        }
        testStart = null;
        return null;
    }

    public Vector3D<float> ScaledOffset(float strength)
    {
        return ClampLength(Offset * strength, MaxScaledOffset);
    }

    public Vector3D<float> ScaledEyeOffset(float strength)
    {
        return ClampLength(EyeOffset * strength, MaxScaledOffset);
    }

    private static float ScaleAngle(float angle, float strength)
    {
        return Math.Clamp(angle * strength, -MaxScaledAngle, MaxScaledAngle);
    }

    public float ScaledRoll(float strength)
    {
        return ScaleAngle(Roll, strength);
    }

    public Matrix4X4<float> RotationMatrix(float strength)
    {
        var roll = Matrix4X4.CreateFromQuaternion(Quaternion<float>.CreateFromAxisAngle(-Vector3D<float>.UnitZ, ScaleAngle(Roll, strength)));
        return roll * Matrix4X4.CreateRotationX(ScaleAngle(Pitch, strength)) * Matrix4X4.CreateRotationY(-ScaleAngle(Yaw, strength));
    }
}