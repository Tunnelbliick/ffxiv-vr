using Silk.NET.Maths;
using System;

namespace FfxivVR;

public class RemotePose
{
    public const double StaleSeconds = 0.5;
    public const double ExpireSeconds = 10;
    private const double RestartSeconds = 2;
    private const float FadeInSeconds = 0.2f;
    private const float FadeOutSeconds = 0.3f;
    private const float SmoothingSeconds = 0.05f;

    private SyncedPose? target;
    private SyncedPose? current;
    private double lastReceived = double.NegativeInfinity;
    private double? lastAdvanced;
    private uint lastSequence;
    private float weight;

    public bool Receive(uint sequence, SyncedPose pose, double now)
    {
        var isNewer = target == null || (int)(sequence - lastSequence) > 0 || now - lastReceived > RestartSeconds;
        if (!isNewer)
        {
            return false;
        }
        target = pose;
        lastSequence = sequence;
        lastReceived = now;
        return true;
    }

    public bool IsExpired(double now)
    {
        return now - lastReceived > ExpireSeconds;
    }

    public (SyncedPose pose, float weight)? Advance(double now)
    {
        var dt = (float)Math.Clamp(now - (lastAdvanced ?? now), 0, 0.25);
        lastAdvanced = now;
        if (target == null)
        {
            return null;
        }
        var fresh = now - lastReceived < StaleSeconds;
        weight = fresh
            ? MathF.Min(1, weight + (dt / FadeInSeconds))
            : MathF.Max(0, weight - (dt / FadeOutSeconds));
        if (weight <= 0)
        {
            current = null;
            return null;
        }
        current = Smooth(current, target, current == null ? 1 : 1 - MathF.Exp(-dt / SmoothingSeconds));
        return (current, weight);
    }

    private static SyncedPose Smooth(SyncedPose? from, SyncedPose to, float amount)
    {
        var result = new SyncedPose
        {
            Regions = to.Regions,
            Head = Slerp(from?.Head, to.Head, amount),
        };
        for (int i = 0; i < result.Bones.Length; i++)
        {
            result.Bones[i] = Slerp(from?.Bones[i], to.Bones[i], amount);
        }
        return result;
    }

    private static Quaternion<float>? Slerp(Quaternion<float>? from, Quaternion<float>? to, float amount)
    {
        if (to is not { } target)
        {
            return null;
        }
        if (from is not { } start)
        {
            return target;
        }
        return Quaternion<float>.Normalize(Quaternion<float>.Slerp(start, target, amount));
    }
}