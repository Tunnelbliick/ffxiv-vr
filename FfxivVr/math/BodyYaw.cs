using Silk.NET.Maths;
using System;

namespace FfxivVR;

public class BodyYaw
{
    public static readonly float MaxHeadTurn = float.DegreesToRadians(45);
    public const float FollowSeconds = 1.5f;
    private static readonly float ResetJump = float.DegreesToRadians(90);
    private const double MaxStep = 0.25;

    private float? yaw;
    private float lastHead;
    private double? lastUpdate;

    public float Update(Quaternion<float> hmdOrientation, bool seated, double now)
    {
        var dt = lastUpdate is double last ? now - last : double.PositiveInfinity;
        lastUpdate = now;
        if (seated)
        {
            yaw = null;
            return 0;
        }
        if (HeadingYaw(hmdOrientation) is not float head)
        {
            return yaw ?? 0;
        }
        float current;
        if (yaw is not float previous || dt > MaxStep || dt < 0 || MathF.Abs(MathFactory.AcuteAngleBetween(lastHead, head)) > ResetJump)
        {
            current = head;
        }
        else
        {
            current = previous + MathFactory.AcuteAngleBetween(previous, head) * (1 - MathF.Exp(-(float)dt / FollowSeconds));
            var turn = MathFactory.AcuteAngleBetween(current, head);
            if (MathF.Abs(turn) > MaxHeadTurn)
            {
                current = head - MathF.CopySign(MaxHeadTurn, turn);
            }
        }
        lastHead = head;
        yaw = current;
        return current;
    }

    public static float? HeadingYaw(Quaternion<float> orientation)
    {
        var forward = Vector3D.Transform(-Vector3D<float>.UnitZ, orientation);
        if (forward.X * forward.X + forward.Z * forward.Z < 1E-4f)
        {
            return null;
        }
        return FacingYaw(forward);
    }

    public static float FacingYaw(Vector3D<float> forward)
    {
        return -MathF.PI / 2 - MathF.Atan2(forward.Z, forward.X);
    }

    public static float ThirdPersonTrackingYaw(Quaternion<float> characterRotation, float bodyYaw)
    {
        return FacingYaw(Vector3D.Transform(Vector3D<float>.UnitZ, characterRotation)) - bodyYaw;
    }
}