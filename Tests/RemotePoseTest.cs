namespace FfxivVR.Tests;

public class RemotePoseTests
{
    private static SyncedPose Pose(float yaw)
    {
        var pose = new SyncedPose { Regions = SyncRegions.Arms, Head = MathFactory.YRotation(yaw) };
        return pose;
    }

    [Test]
    public void FadesInWhileFreshAndOutWhenStale()
    {
        var remote = new RemotePose();
        Assert.That(remote.Advance(0), Is.Null);
        Assert.That(remote.Receive(1, Pose(0), 0), Is.True);

        Assert.That(remote.Advance(0)?.weight ?? 0, Is.EqualTo(0));
        Assert.That(remote.Advance(0.1)!.Value.weight, Is.EqualTo(0.5f).Within(0.01f));
        Assert.That(remote.Advance(0.3)!.Value.weight, Is.EqualTo(1f));

        Assert.That(remote.Advance(0.4)!.Value.weight, Is.EqualTo(1f));
        Assert.That(remote.Advance(0.6)!.Value.weight, Is.LessThan(1f));
        Assert.That(remote.Advance(0.85), Is.Null);
    }

    [Test]
    public void IgnoresOlderSequences()
    {
        var remote = new RemotePose();
        Assert.That(remote.Receive(10, Pose(0), 0), Is.True);
        Assert.That(remote.Receive(9, Pose(1), 0.01), Is.False);
        Assert.That(remote.Receive(11, Pose(1), 0.02), Is.True);
        Assert.That(remote.Receive(uint.MaxValue, Pose(1), 0.03), Is.False);
        Assert.That(remote.Receive(1, Pose(1), 5), Is.True);
    }

    [Test]
    public void SequenceWrapsAround()
    {
        var remote = new RemotePose();
        Assert.That(remote.Receive(uint.MaxValue, Pose(0), 0), Is.True);
        Assert.That(remote.Receive(0, Pose(0), 0.01), Is.True);
    }

    [Test]
    public void ExpiresAfterSilence()
    {
        var remote = new RemotePose();
        remote.Receive(1, Pose(0), 0);
        Assert.That(remote.IsExpired(RemotePose.ExpireSeconds - 1), Is.False);
        Assert.That(remote.IsExpired(RemotePose.ExpireSeconds + 1), Is.True);
    }

    [Test]
    public void SmoothsTowardsTheLatestPose()
    {
        var remote = new RemotePose();
        remote.Receive(1, Pose(0), 0);
        remote.Advance(0);
        remote.Advance(0.2);
        remote.Receive(2, Pose(1), 0.2);
        var halfway = remote.Advance(0.21)!.Value.pose.Head!.Value.ToYawPitchRoll().yaw;
        Assert.That(halfway, Is.GreaterThan(0).And.LessThan(1));
        var settled = remote.Advance(0.5)!.Value.pose.Head!.Value.ToYawPitchRoll().yaw;
        Assert.That(settled, Is.EqualTo(1).Within(0.01f));
    }
}