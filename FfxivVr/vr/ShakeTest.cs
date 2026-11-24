using Silk.NET.Maths;
using System;

namespace FfxivVR;

[Flags]
public enum ShakeSource
{
    LookAt = 1,
    CameraPosition = 2,
    CameraRotation = 4,
    CameraRoll = 8,
    Quake = CameraPosition | CameraRotation | CameraRoll,
}

public static class ShakeTest
{
    public const float Seconds = 1.2f;
    private const float DecaySeconds = 0.3f;
    public const float Height = 0.06f;
    public static readonly float Angle = float.DegreesToRadians(1);

    private static float Wave(float t, float amplitude, float hertz)
    {
        return amplitude * MathF.Exp(-t / DecaySeconds) * MathF.Sin(2 * MathF.PI * hertz * t);
    }

    public static float Bounce(float t) => Wave(t, Height, 9);
    public static float Pitch(float t) => Wave(t, Angle, 7);
    public static float Yaw(float t) => Wave(t, Angle * 0.6f, 5);
    public static float Roll(float t) => Wave(t, Angle, 6);

    public static Vector3D<float> ShakeLookAt(Vector3D<float> lookAt, ShakeSource source, float t)
    {
        return source.HasFlag(ShakeSource.LookAt) ? lookAt + new Vector3D<float>(0, Bounce(t), 0) : lookAt;
    }

    private static Quaternion<float> Turn(Vector3D<float> from, Vector3D<float> to)
    {
        var a = Vector3D.Normalize(from);
        var b = Vector3D.Normalize(to);
        var axis = Vector3D.Cross(a, b);
        return Quaternion<float>.Normalize(new Quaternion<float>(axis.X, axis.Y, axis.Z, 1 + Vector3D.Dot(a, b)));
    }

    public static Matrix4X4<float> ShakeView(Matrix4X4<float> view, Vector3D<float> cameraPosition, Vector3D<float> lookAt, Vector3D<float> shakenLookAt, ShakeSource source, float t)
    {
        if (!Matrix4X4.Invert(view, out var world))
        {
            return view;
        }
        var eye = new Vector3D<float>(world.M41, world.M42, world.M43);
        var rotation = world;
        rotation.M41 = 0;
        rotation.M42 = 0;
        rotation.M43 = 0;
        if (shakenLookAt != lookAt)
        {
            rotation *= Matrix4X4.CreateFromQuaternion(Turn(lookAt - cameraPosition, shakenLookAt - cameraPosition));
        }
        var local = Matrix4X4<float>.Identity;
        if (source.HasFlag(ShakeSource.CameraRoll))
        {
            local *= Matrix4X4.CreateFromQuaternion(Quaternion<float>.CreateFromAxisAngle(-Vector3D<float>.UnitZ, Roll(t)));
        }
        if (source.HasFlag(ShakeSource.CameraRotation))
        {
            local *= Matrix4X4.CreateRotationX(Pitch(t)) * Matrix4X4.CreateRotationY(-Yaw(t));
        }
        if (source.HasFlag(ShakeSource.CameraPosition))
        {
            eye.Y += Bounce(t);
        }
        var shaken = local * rotation;
        shaken.M41 = eye.X;
        shaken.M42 = eye.Y;
        shaken.M43 = eye.Z;
        return Matrix4X4.Invert(shaken, out var shakenView) ? shakenView : view;
    }
}