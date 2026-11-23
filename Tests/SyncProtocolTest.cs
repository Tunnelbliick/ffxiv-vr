using Silk.NET.Maths;

namespace FfxivVR.Tests;

public class SyncProtocolTests
{
    private static float AngleBetween(Quaternion<float> a, Quaternion<float> b)
    {
        var dot = MathF.Abs(a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W);
        return 2 * MathF.Acos(MathF.Min(1, dot));
    }

    [Test]
    public void PackedQuaternionsRoundTrip()
    {
        var random = new Random(1234);
        for (int i = 0; i < 1000; i++)
        {
            var rotation = Quaternion<float>.Normalize(new Quaternion<float>(
                random.NextSingle() * 2 - 1,
                random.NextSingle() * 2 - 1,
                random.NextSingle() * 2 - 1,
                random.NextSingle() * 2 - 1));
            var unpacked = SyncProtocol.UnpackQuaternion(SyncProtocol.PackQuaternion(rotation));
            Assert.That(AngleBetween(rotation, unpacked), Is.LessThan(float.DegreesToRadians(0.5f)));
        }
        Assert.That(AngleBetween(Quaternion<float>.Identity, SyncProtocol.UnpackQuaternion(SyncProtocol.PackQuaternion(Quaternion<float>.Identity))), Is.LessThan(0.001f));
    }

    [Test]
    public void PosePacketRoundTrip()
    {
        var pose = new SyncedPose
        {
            Regions = SyncRegions.Arms,
            Head = MathFactory.YRotation(0.5f),
        };
        var armIndex = SyncBones.IndexOf(HumanBones.ArmLeft);
        var fingerIndex = SyncBones.IndexOf(HumanBones.PinkyFingerRightB);
        pose.Bones[armIndex] = MathFactory.XRotation(0.25f);
        pose.Bones[fingerIndex] = MathFactory.ZRotation(-1f);

        var buffer = new byte[SyncProtocol.MaxPacketSize];
        var length = SyncProtocol.WritePose(buffer, token: 77, room: 1234, sender: 5678, sequence: 42, pose);

        Assert.That(length, Is.EqualTo(SyncProtocol.PoseHeaderSize + 4 * 3));
        Assert.That(SyncProtocol.TryReadPose(buffer.AsSpan(0, length), out var message), Is.True);
        Assert.That(message!.Room, Is.EqualTo(1234UL));
        Assert.That(message.Sender, Is.EqualTo(5678UL));
        Assert.That(message.Sequence, Is.EqualTo(42U));
        Assert.That(message.Pose.Regions, Is.EqualTo(SyncRegions.Arms));
        Assert.That(AngleBetween(message.Pose.Head!.Value, pose.Head.Value), Is.LessThan(0.01f));
        Assert.That(AngleBetween(message.Pose.Bones[armIndex]!.Value, pose.Bones[armIndex]!.Value), Is.LessThan(0.01f));
        Assert.That(AngleBetween(message.Pose.Bones[fingerIndex]!.Value, pose.Bones[fingerIndex]!.Value), Is.LessThan(0.01f));
        Assert.That(message.Pose.Bones.Count(b => b != null), Is.EqualTo(2));
    }

    [Test]
    public void RejectsMalformedPosePackets()
    {
        var pose = new SyncedPose { Regions = SyncRegions.Arms };
        pose.Bones[0] = Quaternion<float>.Identity;
        var buffer = new byte[SyncProtocol.MaxPacketSize];
        var length = SyncProtocol.WritePose(buffer, 1, 2, 3, 4, pose);

        Assert.That(SyncProtocol.TryReadPose(buffer.AsSpan(0, length - 1), out _), Is.False);
        Assert.That(SyncProtocol.TryReadPose(buffer.AsSpan(0, length + 4), out _), Is.False);

        buffer[34 + 7] = 0x80;
        Assert.That(SyncProtocol.TryReadPose(buffer.AsSpan(0, length), out _), Is.False);
    }

    [Test]
    public void BoneTableFitsMask()
    {
        Assert.That(SyncBones.All.Length, Is.LessThanOrEqualTo(64));
        Assert.That(SyncBones.All.Select(b => b.Name).Distinct().Count(), Is.EqualTo(SyncBones.All.Length));
        var maxPose = new SyncedPose { Head = Quaternion<float>.Identity };
        for (int i = 0; i < maxPose.Bones.Length; i++)
        {
            maxPose.Bones[i] = Quaternion<float>.Identity;
        }
        Assert.That(SyncProtocol.WritePose(new byte[SyncProtocol.MaxPacketSize], 0, 0, 0, 0, maxPose), Is.LessThanOrEqualTo(SyncProtocol.MaxPacketSize));
    }

    [Test]
    public void IdsAreStableAndDistinct()
    {
        var room = SyncProtocol.RoomId(world: 40, territory: 129, instance: 0, ward: -1);
        Assert.That(SyncProtocol.RoomId(40, 129, 0, -1), Is.EqualTo(room));
        Assert.That(SyncProtocol.RoomId(40, 129, 1, -1), Is.Not.EqualTo(room));
        var player = SyncProtocol.PlayerId(1000, room);
        Assert.That(SyncProtocol.PlayerId(1000, room), Is.EqualTo(player));
        Assert.That(SyncProtocol.PlayerId(1001, room), Is.Not.EqualTo(player));
        Assert.That(SyncProtocol.PlayerId(1000, room + 1), Is.Not.EqualTo(player));
    }

    [Test]
    public void YawPitchRollRoundTrip()
    {
        var angles = new[] { -1.2f, -0.4f, 0f, 0.3f, 1.1f };
        foreach (var yaw in angles)
        {
            foreach (var pitch in angles)
            {
                foreach (var roll in angles)
                {
                    var (y, p, r) = Quaternion<float>.CreateFromYawPitchRoll(yaw, pitch, roll).ToYawPitchRoll();
                    Assert.That(y, Is.EqualTo(yaw).Within(0.001f));
                    Assert.That(p, Is.EqualTo(pitch).Within(0.001f));
                    Assert.That(r, Is.EqualTo(roll).Within(0.001f));
                }
            }
        }
    }

    [Test]
    public void HeadRotationIsNeutralWhenLookingWhereTheCharacterFaces()
    {
        var skeletonYaw = 0.3f;
        var head = SyncSkeletonModifier.ComputeHeadRotation(MathFactory.YRotation(skeletonYaw), skeletonYaw + MathF.PI, Quaternion<float>.Identity);
        Assert.That(AngleBetween(head, Quaternion<float>.Identity), Is.LessThan(0.001f));

        var turned = SyncSkeletonModifier.ComputeHeadRotation(MathFactory.YRotation(skeletonYaw), skeletonYaw + MathF.PI, MathFactory.YRotation(0.5f));
        Assert.That(turned.ToYawPitchRoll().yaw, Is.EqualTo(0.5f).Within(0.001f));
    }

    [Test]
    public void HeadRotationIsClamped()
    {
        var clamped = SyncSkeletonModifier.ClampHeadRotation(MathFactory.YRotation(float.DegreesToRadians(170)));
        Assert.That(clamped.ToYawPitchRoll().yaw, Is.EqualTo(float.DegreesToRadians(80)).Within(0.001f));
        var small = MathFactory.XRotation(0.2f);
        Assert.That(AngleBetween(SyncSkeletonModifier.ClampHeadRotation(small), small), Is.LessThan(0.001f));
    }

    [Test]
    public void ParsesServerAddresses()
    {
        Assert.That(PoseSyncClient.ParseAddress("vr.example.com:1234"), Is.EqualTo(("vr.example.com", 1234)));
        Assert.That(PoseSyncClient.ParseAddress(" vr.example.com "), Is.EqualTo(("vr.example.com", SyncProtocol.DefaultPort)));
        Assert.That(PoseSyncClient.ParseAddress("[::1]:9000"), Is.EqualTo(("::1", 9000)));
        Assert.That(PoseSyncClient.ParseAddress(""), Is.Null);
        Assert.That(PoseSyncClient.ParseAddress("bad host/path"), Is.Null);
    }
}