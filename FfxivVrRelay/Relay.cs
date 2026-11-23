using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;

namespace FfxivVrRelay;

public sealed class Relay
{
    public delegate void Sender(ReadOnlySpan<byte> data, IPEndPoint to);

    public const ushort Magic = 0x5646;
    public const byte Version = 1;
    public const byte KeepaliveType = 1;
    public const byte PoseType = 2;
    public const byte WelcomeType = 3;
    public const int KeepaliveSize = 20;
    public const int WelcomeSize = 14;
    public const int PoseHeaderSize = 42;
    public const int MaxPacketSize = 512;
    public const int MaxClients = 5000;
    public const int MaxRoomSize = 64;
    public const int MaxPacketsPerSecond = 60;
    public const long ClientTimeoutMs = 15_000;

    private sealed class Client
    {
        public ulong Room;
        public long LastSeen;
        public long WindowStart;
        public int PacketsInWindow;
    }

    private readonly byte[] secret;
    private readonly Dictionary<IPEndPoint, Client> clients = new();
    private readonly Dictionary<ulong, HashSet<IPEndPoint>> rooms = new();

    public Relay() : this(RandomNumberGenerator.GetBytes(32)) { }

    public Relay(byte[] secret)
    {
        this.secret = secret;
    }

    public int ClientCount => clients.Count;
    public int RoomCount => rooms.Count;

    public ulong TokenFor(IPEndPoint endpoint)
    {
        Span<byte> input = stackalloc byte[18];
        var address = endpoint.Address.IsIPv4MappedToIPv6 ? endpoint.Address.MapToIPv4() : endpoint.Address;
        address.TryWriteBytes(input, out var written);
        BinaryPrimitives.WriteUInt16LittleEndian(input[written..], (ushort)endpoint.Port);
        var token = BinaryPrimitives.ReadUInt64LittleEndian(HMACSHA256.HashData(secret, input[..(written + 2)]));
        return token == 0 ? 1 : token;
    }

    public void Handle(ReadOnlySpan<byte> data, IPEndPoint from, long now, Sender send)
    {
        if (data.Length < 4 || data.Length > MaxPacketSize || BinaryPrimitives.ReadUInt16LittleEndian(data) != Magic || data[2] != Version)
        {
            return;
        }
        var type = data[3];
        if (type == KeepaliveType && data.Length >= KeepaliveSize)
        {
            HandleKeepalive(data, from, now, send);
        }
        else if (type == PoseType && data.Length >= PoseHeaderSize)
        {
            HandlePose(data, from, now, send);
        }
    }

    private void HandleKeepalive(ReadOnlySpan<byte> data, IPEndPoint from, long now, Sender send)
    {
        var expected = TokenFor(from);
        var room = ReadRoom(data);
        if (ReadToken(data) == expected)
        {
            Touch(from, room, now);
        }
        SendWelcome(from, expected, OthersIn(room, from), send);
    }

    private void HandlePose(ReadOnlySpan<byte> data, IPEndPoint from, long now, Sender send)
    {
        var expected = TokenFor(from);
        if (ReadToken(data) != expected)
        {
            SendWelcome(from, expected, OthersIn(ReadRoom(data), from), send);
            return;
        }
        var room = ReadRoom(data);
        if (!Touch(from, room, now) || !rooms.TryGetValue(room, out var members))
        {
            return;
        }
        Span<byte> forward = stackalloc byte[data.Length];
        data.CopyTo(forward);
        forward[4..12].Clear();
        foreach (var member in members)
        {
            if (!member.Equals(from))
            {
                send(forward, member);
            }
        }
    }

    private int OthersIn(ulong room, IPEndPoint endpoint)
    {
        if (!rooms.TryGetValue(room, out var members))
        {
            return 0;
        }
        return members.Count - (members.Contains(endpoint) ? 1 : 0);
    }

    private static void SendWelcome(IPEndPoint to, ulong token, int others, Sender send)
    {
        Span<byte> welcome = stackalloc byte[WelcomeSize];
        BinaryPrimitives.WriteUInt16LittleEndian(welcome, Magic);
        welcome[2] = Version;
        welcome[3] = WelcomeType;
        BinaryPrimitives.WriteUInt64LittleEndian(welcome[4..], token);
        BinaryPrimitives.WriteUInt16LittleEndian(welcome[12..], (ushort)Math.Min(others, ushort.MaxValue));
        send(welcome, to);
    }

    private static ulong ReadToken(ReadOnlySpan<byte> data) => BinaryPrimitives.ReadUInt64LittleEndian(data[4..]);

    private static ulong ReadRoom(ReadOnlySpan<byte> data) => BinaryPrimitives.ReadUInt64LittleEndian(data[12..]);

    private bool Touch(IPEndPoint from, ulong room, long now)
    {
        if (!clients.TryGetValue(from, out var client))
        {
            if (clients.Count >= MaxClients || !JoinRoom(from, room))
            {
                return false;
            }
            client = new Client { Room = room, WindowStart = now };
            clients[from] = client;
        }
        else if (client.Room != room)
        {
            LeaveRoom(from, client.Room);
            if (!JoinRoom(from, room))
            {
                clients.Remove(from);
                return false;
            }
            client.Room = room;
        }
        client.LastSeen = now;
        if (now - client.WindowStart >= 1000)
        {
            client.WindowStart = now;
            client.PacketsInWindow = 0;
        }
        client.PacketsInWindow++;
        return client.PacketsInWindow <= MaxPacketsPerSecond;
    }

    private bool JoinRoom(IPEndPoint endpoint, ulong room)
    {
        if (!rooms.TryGetValue(room, out var members))
        {
            members = new HashSet<IPEndPoint>();
            rooms[room] = members;
        }
        if (members.Count >= MaxRoomSize)
        {
            return false;
        }
        members.Add(endpoint);
        return true;
    }

    private void LeaveRoom(IPEndPoint endpoint, ulong room)
    {
        if (!rooms.TryGetValue(room, out var members))
        {
            return;
        }
        members.Remove(endpoint);
        if (members.Count == 0)
        {
            rooms.Remove(room);
        }
    }

    public void Cleanup(long now)
    {
        List<IPEndPoint>? expired = null;
        foreach (var (endpoint, client) in clients)
        {
            if (now - client.LastSeen > ClientTimeoutMs)
            {
                (expired ??= new()).Add(endpoint);
            }
        }
        if (expired == null)
        {
            return;
        }
        foreach (var endpoint in expired)
        {
            LeaveRoom(endpoint, clients[endpoint].Room);
            clients.Remove(endpoint);
        }
    }
}