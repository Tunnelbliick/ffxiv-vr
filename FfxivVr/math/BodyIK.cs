using Silk.NET.Maths;
using System;

namespace FfxivVR;

public static class BodyIK
{
    public static readonly Vector3D<float> EyeOffset = new(0, 0.075f, -0.08f);
    public const float MaxHorizontalLean = 0.45f;
    public const float MaxRise = 0.05f;
    public const float MaxDrop = 0.45f;
    public static readonly float MaxShoulderAngle = float.DegreesToRadians(25);
    public static readonly float[] LeanShares = [0.45f, 0.35f, 0.2f];
    public static readonly float[] YawShares = [0.1f, 0.1f, 0.1f];

    private const int LeanIterations = 6;
    private const float ShoulderReachStart = 0.85f;
    private const float ShoulderReachRange = 0.3f;
    private const float ShoulderReachShare = 0.4f;
    private const float ShoulderRaiseShare = 0.35f;
    private const float Epsilon = 1E-5f;

    public static Vector3D<float> HeadDisplacement(Vector3D<float> eyeCenter, Quaternion<float> hmdOrientation)
    {
        return eyeCenter - Vector3D.Transform(EyeOffset, hmdOrientation) + EyeOffset;
    }

    public static Vector3D<float> ClampLean(Vector3D<float> displacement)
    {
        var horizontal = new Vector3D<float>(displacement.X, 0, displacement.Z);
        var length = horizontal.Length;
        if (length > MaxHorizontalLean)
        {
            horizontal *= MaxHorizontalLean / length;
        }
        return horizontal + new Vector3D<float>(0, Math.Clamp(displacement.Y, -MaxDrop, MaxRise), 0);
    }

    public static Quaternion<float>[] SolveSpine(ReadOnlySpan<Vector3D<float>> joints, Vector3D<float> end, Vector3D<float> endOffset, Vector3D<float> target, float yaw)
    {
        if (joints.Length != LeanShares.Length)
        {
            throw new ArgumentException($"Expected {LeanShares.Length} spine joints, got {joints.Length}");
        }
        var points = new Vector3D<float>[joints.Length + 1];
        joints.CopyTo(points);
        points[joints.Length] = end;
        var deltas = new Quaternion<float>[joints.Length];
        Array.Fill(deltas, Quaternion<float>.Identity);

        for (int i = 0; i < joints.Length; i++)
        {
            RotateAt(points, deltas, i, Quaternion<float>.CreateFromAxisAngle(Vector3D<float>.UnitY, yaw * YawShares[i]));
        }
        for (int iteration = 0; iteration < LeanIterations; iteration++)
        {
            var from = points[joints.Length] + endOffset - points[0];
            var to = target - points[0];
            var axis = Vector3D.Cross(from, to);
            if (from.Length < Epsilon || to.Length < Epsilon || axis.Length < Epsilon)
            {
                break;
            }
            axis = Vector3D.Normalize(axis);
            var angle = MathF.Acos(Math.Clamp(Vector3D.Dot(Vector3D.Normalize(from), Vector3D.Normalize(to)), -1f, 1f));
            for (int i = 0; i < joints.Length; i++)
            {
                RotateAt(points, deltas, i, Quaternion<float>.CreateFromAxisAngle(axis, angle * LeanShares[i]));
            }
        }
        return deltas;
    }

    private static void RotateAt(Vector3D<float>[] points, Quaternion<float>[] deltas, int joint, Quaternion<float> rotation)
    {
        for (int k = joint + 1; k < points.Length; k++)
        {
            points[k] = points[joint] + Vector3D.Transform(points[k] - points[joint], rotation);
        }
        for (int k = joint; k < deltas.Length; k++)
        {
            deltas[k] = Quaternion<float>.Normalize(rotation * deltas[k]);
        }
    }

    public static Quaternion<float> ShoulderAssist(Vector3D<float> shoulder, Vector3D<float> arm, float armLength, Vector3D<float> target)
    {
        if (armLength < Epsilon)
        {
            return Quaternion<float>.Identity;
        }
        var reach = (target - arm).Length;
        var overflow = Math.Clamp((reach - ShoulderReachStart * armLength) / (ShoulderReachRange * armLength), 0f, 1f);
        var raise = Math.Clamp((target.Y - arm.Y) / armLength, 0f, 1f);
        var share = MathF.Max(overflow * ShoulderReachShare, raise * ShoulderRaiseShare);
        if (share <= 0 || (arm - shoulder).Length < Epsilon || (target - shoulder).Length < Epsilon)
        {
            return Quaternion<float>.Identity;
        }
        var swing = MathFactory.RotateOnto(arm - shoulder, target - shoulder);
        return LimitAngle(Quaternion<float>.Slerp(Quaternion<float>.Identity, swing, share), MaxShoulderAngle);
    }

    public static float Angle(Quaternion<float> rotation)
    {
        return 2 * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion<float>.Normalize(rotation).W), 0f, 1f));
    }

    private static Quaternion<float> LimitAngle(Quaternion<float> rotation, float maxAngle)
    {
        var angle = Angle(rotation);
        if (angle <= maxAngle)
        {
            return rotation;
        }
        return Quaternion<float>.Slerp(Quaternion<float>.Identity, rotation, maxAngle / angle);
    }
}