using Silk.NET.Maths;

namespace FfxivVR.Tests;

public class BodyIKTests
{
    private static readonly Vector3D<float>[] Joints = [new(0, 0, 0), new(0, 0.15f, 0), new(0, 0.3f, 0)];
    private static readonly Vector3D<float> Neck = new(0, 0.45f, 0);
    private static readonly Vector3D<float> FaceOffset = new(0, 0.1f, 0);
    private static readonly Vector3D<float> RestEffector = Neck + FaceOffset;

    private static Vector3D<float> Effector(Quaternion<float>[] deltas)
    {
        var points = new Vector3D<float>[] { Joints[0], Joints[1], Joints[2], Neck };
        var solved = new Vector3D<float>[points.Length];
        solved[0] = points[0];
        for (int i = 0; i < deltas.Length; i++)
        {
            solved[i + 1] = solved[i] + Vector3D.Transform(points[i + 1] - points[i], deltas[i]);
        }
        return solved[^1] + FaceOffset;
    }

    private static float OwnAngle(Quaternion<float>[] deltas, int joint)
    {
        var parent = joint == 0 ? Quaternion<float>.Identity : deltas[joint - 1];
        return BodyIK.Angle(deltas[joint] * parent.Inverse());
    }

    [Test]
    public void NoDisplacementKeepsTheSpine()
    {
        var deltas = BodyIK.SolveSpine(Joints, Neck, FaceOffset, RestEffector, 0);
        foreach (var delta in deltas)
        {
            Assert.That(BodyIK.Angle(delta), Is.LessThan(1E-3f));
        }
    }

    [Test]
    public void LeaningForwardBendsTheSpineTowardTheHead()
    {
        var target = RestEffector + new Vector3D<float>(0, -0.05f, 0.25f);
        var deltas = BodyIK.SolveSpine(Joints, Neck, FaceOffset, target, 0);
        var effector = Effector(deltas);

        var reached = Vector3D.Normalize(effector - Joints[0]);
        var wanted = Vector3D.Normalize(target - Joints[0]);
        Assert.That(MathF.Acos(Math.Clamp(Vector3D.Dot(reached, wanted), -1f, 1f)), Is.LessThan(float.DegreesToRadians(1)));
        Assert.That(effector.Z, Is.GreaterThan(0.2f));
        Assert.That(OwnAngle(deltas, 0), Is.GreaterThan(OwnAngle(deltas, 2)));
    }

    [Test]
    public void LeaningSidewaysStaysInTheSidePlane()
    {
        var target = RestEffector + new Vector3D<float>(0.2f, 0, 0);
        var effector = Effector(BodyIK.SolveSpine(Joints, Neck, FaceOffset, target, 0));
        Assert.That(effector.X, Is.GreaterThan(0.15f));
        Assert.That(MathF.Abs(effector.Z), Is.LessThan(1E-3f));
    }

    [Test]
    public void HeadYawTwistsTheChestPartially()
    {
        var yaw = float.DegreesToRadians(60);
        var deltas = BodyIK.SolveSpine(Joints, Neck, FaceOffset, RestEffector, yaw);
        var expected = yaw * BodyIK.YawShares.Sum();
        Assert.That(deltas[^1].GetYaw(), Is.EqualTo(expected).Within(1E-3f));
    }

    [Test]
    public void TurningTheHeadIsNotALean()
    {
        var orientation = Quaternion<float>.CreateFromYawPitchRoll(float.DegreesToRadians(40), float.DegreesToRadians(-60), float.DegreesToRadians(10));
        var pivot = -BodyIK.EyeOffset;
        var eyeCenter = pivot + Vector3D.Transform(BodyIK.EyeOffset, orientation);
        var displacement = BodyIK.HeadDisplacement(eyeCenter, orientation);
        Assert.That(displacement.Length, Is.LessThan(1E-5f));

        var leaned = BodyIK.HeadDisplacement(eyeCenter + new Vector3D<float>(0, 0, -0.2f), orientation);
        Assert.That(leaned.Z, Is.EqualTo(-0.2f).Within(1E-5f));
    }

    [Test]
    public void LeanIsClamped()
    {
        var clamped = BodyIK.ClampLean(new Vector3D<float>(1, 1, 1), false);
        Assert.That(new Vector3D<float>(clamped.X, 0, clamped.Z).Length, Is.EqualTo(BodyIK.MaxHorizontalLean).Within(1E-5f));
        Assert.That(clamped.Y, Is.EqualTo(BodyIK.MaxRise));
        Assert.That(BodyIK.ClampLean(new Vector3D<float>(0, -2, 0), false).Y, Is.EqualTo(-BodyIK.MaxDrop));
        var small = new Vector3D<float>(0.1f, -0.1f, 0.05f);
        Assert.That(BodyIK.ClampLean(small, false), Is.EqualTo(small));
    }

    [Test]
    public void SeatedLeanIgnoresHeight()
    {
        var slouched = BodyIK.ClampLean(new Vector3D<float>(0.1f, -0.4f, 0.2f), true);
        Assert.That(slouched, Is.EqualTo(new Vector3D<float>(0.1f, 0, 0.2f)));
    }

    [Test]
    public void ShoulderOnlyMovesForFarOrRaisedHands()
    {
        var shoulder = new Vector3D<float>(0.03f, 1.4f, 0);
        var arm = new Vector3D<float>(0.18f, 1.4f, 0);
        var armLength = 0.55f;

        var resting = BodyIK.ShoulderAssist(shoulder, arm, armLength, arm + new Vector3D<float>(0.05f, -0.4f, 0.1f));
        Assert.That(BodyIK.Angle(resting), Is.LessThan(1E-4f));

        var raised = BodyIK.ShoulderAssist(shoulder, arm, armLength, arm + new Vector3D<float>(0.05f, 0.5f, 0.1f));
        Assert.That(BodyIK.Angle(raised), Is.GreaterThan(float.DegreesToRadians(5)));
        Assert.That(BodyIK.Angle(raised), Is.LessThanOrEqualTo(BodyIK.MaxShoulderAngle + 1E-4f));
        Assert.That(Vector3D.Transform(arm - shoulder, raised).Y, Is.GreaterThan(0));

        var reaching = BodyIK.ShoulderAssist(shoulder, arm, armLength, arm + new Vector3D<float>(0, 0, 0.7f));
        Assert.That(Vector3D.Transform(arm - shoulder, reaching).Z, Is.GreaterThan(0));
    }

    [Test]
    public void HeadClampFollowsTheChest()
    {
        var chest = MathFactory.XRotation(float.DegreesToRadians(30));
        var head = MathFactory.XRotation(float.DegreesToRadians(80));
        var clamped = SyncSkeletonModifier.ClampHeadRotation(head, chest);
        Assert.That(BodyIK.Angle(clamped * head.Inverse()), Is.LessThan(1E-3f));

        var absolute = SyncSkeletonModifier.ClampHeadRotation(head, Quaternion<float>.Identity);
        Assert.That(BodyIK.Angle(absolute), Is.EqualTo(float.DegreesToRadians(60)).Within(1E-3f));
    }
}