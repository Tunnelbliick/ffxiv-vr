using FfxivVrRelay;
using Silk.NET.Maths;
using System.Buffers.Binary;
using System.Net;

namespace FfxivVR.Tests;

public class RelayTests
{
    private readonly List<(IPEndPoint to, byte[] data)> sent = new();
    private Relay relay = null!;
    private static readonly IPEndPoint Alice = new(IPAddress.Parse("10.0.0.1"), 5000);
    private static readonly IPEndPoint Bob = new(IPAddress.Parse("10.0.0.2"), 6000);
    private static readonly IPEndPoint Carol = new(IPAddress.Parse("10.0.0.3"), 7000);

    [SetUp]
    public void SetUp()
    {
        sent.Clear();
        relay = new Relay(new byte[32]);
    }

    private void Receive(byte[] data, IPEndPoint from, long now = 0)
    {
        relay.Handle(data, from, now, (packet, to) => sent.Add((to, packet.ToArray())));
    }

    private ulong Join(IPEndPoint client, ulong room, long now = 0)
    {
        var token = relay.TokenFor(client);
        var keepalive = new byte[SyncProtocol.KeepaliveSize];
        SyncProtocol.WriteKeepalive(keepalive, token, room);
        Receive(keepalive, client, now);
        return token;
    }

    private static byte[] PosePacket(ulong token, ulong room)
    {
        var pose = new SyncedPose { Regions = SyncRegions.Arms, Head = Quaternion<float>.Identity };
        var buffer = new byte[SyncProtocol.MaxPacketSize];
        var length = SyncProtocol.WritePose(buffer, token, room, sender: 99, sequence: 1, pose);
        return buffer[..length];
    }

    [Test]
    public void KeepaliveIsAnsweredWithTheClientsToken()
    {
        var keepalive = new byte[SyncProtocol.KeepaliveSize];
        SyncProtocol.WriteKeepalive(keepalive, 0, 1);
        Receive(keepalive, Alice);

        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.That(sent[0].to, Is.EqualTo(Alice));
        Assert.That(SyncProtocol.TryReadWelcome(sent[0].data, out var token, out _), Is.True);
        Assert.That(token, Is.EqualTo(relay.TokenFor(Alice)));
        Assert.That(relay.ClientCount, Is.EqualTo(0));
    }

    [Test]
    public void PosesAreForwardedToTheRoomWithoutTheToken()
    {
        var aliceToken = Join(Alice, room: 1);
        Join(Bob, room: 1);
        Join(Carol, room: 2);
        sent.Clear();

        Receive(PosePacket(aliceToken, 1), Alice);

        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.That(sent[0].to, Is.EqualTo(Bob));
        Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(sent[0].data.AsSpan(SyncProtocol.TokenOffset)), Is.EqualTo(0UL));
        Assert.That(SyncProtocol.TryReadPose(sent[0].data, out var message), Is.True);
        Assert.That(message!.Sender, Is.EqualTo(99UL));
    }

    [Test]
    public void PosesWithAWrongTokenAreNotForwarded()
    {
        Join(Bob, room: 1);
        sent.Clear();

        Receive(PosePacket(relay.TokenFor(Bob), 1), Alice);

        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.That(sent[0].to, Is.EqualTo(Alice));
        Assert.That(SyncProtocol.TryReadWelcome(sent[0].data, out _, out _), Is.True);
    }

    [Test]
    public void ClientsMoveBetweenRoomsAndExpire()
    {
        var aliceToken = Join(Alice, room: 1);
        Join(Bob, room: 1);
        Join(Bob, room: 2);
        sent.Clear();

        Receive(PosePacket(aliceToken, 1), Alice);
        Assert.That(sent, Is.Empty);
        Assert.That(relay.RoomCount, Is.EqualTo(2));

        relay.Cleanup(Relay.ClientTimeoutMs + 1);
        Assert.That(relay.ClientCount, Is.EqualTo(0));
        Assert.That(relay.RoomCount, Is.EqualTo(0));
    }

    [Test]
    public void FloodingClientsAreRateLimited()
    {
        var aliceToken = Join(Alice, room: 1);
        Join(Bob, room: 1);
        sent.Clear();

        for (int i = 0; i < Relay.MaxPacketsPerSecond * 2; i++)
        {
            Receive(PosePacket(aliceToken, 1), Alice, now: 10);
        }

        Assert.That(sent.Count, Is.LessThan(Relay.MaxPacketsPerSecond));
    }

    [Test]
    public void WelcomeCountsTheOtherClientsInTheRoom()
    {
        Join(Alice, room: 1);
        Join(Bob, room: 1);
        Join(Carol, room: 2);
        sent.Clear();

        Join(Alice, room: 1);
        Assert.That(SyncProtocol.TryReadWelcome(sent[^1].data, out _, out var alicePeers), Is.True);
        Assert.That(alicePeers, Is.EqualTo(1));

        Join(Carol, room: 2);
        Assert.That(SyncProtocol.TryReadWelcome(sent[^1].data, out _, out var carolPeers), Is.True);
        Assert.That(carolPeers, Is.EqualTo(0));

        Join(Carol, room: 1);
        Join(Alice, room: 1);
        Assert.That(SyncProtocol.TryReadWelcome(sent[^1].data, out _, out var afterMove), Is.True);
        Assert.That(afterMove, Is.EqualTo(2));
    }

    [Test]
    public void ProtocolConstantsMatchThePlugin()
    {
        Assert.That(Relay.Magic, Is.EqualTo(SyncProtocol.Magic));
        Assert.That(Relay.Version, Is.EqualTo(SyncProtocol.Version));
        Assert.That(Relay.KeepaliveType, Is.EqualTo((byte)SyncPacketType.Keepalive));
        Assert.That(Relay.PoseType, Is.EqualTo((byte)SyncPacketType.Pose));
        Assert.That(Relay.WelcomeType, Is.EqualTo((byte)SyncPacketType.Welcome));
        Assert.That(Relay.KeepaliveSize, Is.EqualTo(SyncProtocol.KeepaliveSize));
        Assert.That(Relay.WelcomeSize, Is.EqualTo(SyncProtocol.WelcomeSize));
        Assert.That(Relay.PoseHeaderSize, Is.EqualTo(SyncProtocol.PoseHeaderSize));
        Assert.That(Relay.MaxPacketSize, Is.EqualTo(SyncProtocol.MaxPacketSize));
    }
}