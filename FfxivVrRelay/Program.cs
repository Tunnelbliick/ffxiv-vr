using FfxivVrRelay;
using System.Net;
using System.Net.Sockets;

var portSetting = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("RELAY_PORT");
var port = int.TryParse(portSetting, out var parsedPort) ? parsedPort : 7850;

using var socket = CreateSocket(port);
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};
AppDomain.CurrentDomain.ProcessExit += (_, _) => cancellation.Cancel();

var relay = new Relay();
var buffer = new byte[Relay.MaxPacketSize + 1];
EndPoint any = socket.AddressFamily == AddressFamily.InterNetworkV6
    ? new IPEndPoint(IPAddress.IPv6Any, 0)
    : new IPEndPoint(IPAddress.Any, 0);
var lastCleanup = Environment.TickCount64;
var lastStats = Environment.TickCount64;
Relay.Sender send = (data, to) =>
{
    try
    {
        socket.SendTo(data, SocketFlags.None, to);
    }
    catch (SocketException)
    {
    }
};

Console.WriteLine($"FFXIV VR relay listening on UDP port {port} ({socket.AddressFamily})");
while (!cancellation.IsCancellationRequested)
{
    SocketReceiveFromResult result;
    try
    {
        result = await socket.ReceiveFromAsync(buffer, SocketFlags.None, any, cancellation.Token);
    }
    catch (OperationCanceledException)
    {
        break;
    }
    catch (SocketException)
    {
        continue;
    }
    var now = Environment.TickCount64;
    relay.Handle(buffer.AsSpan(0, result.ReceivedBytes), (IPEndPoint)result.RemoteEndPoint, now, send);
    if (now - lastCleanup >= 1000)
    {
        relay.Cleanup(now);
        lastCleanup = now;
    }
    if (now - lastStats >= 60_000)
    {
        Console.WriteLine($"clients={relay.ClientCount} rooms={relay.RoomCount}");
        lastStats = now;
    }
}
Console.WriteLine("FFXIV VR relay stopped");

static Socket CreateSocket(int port)
{
    if (Socket.OSSupportsIPv6)
    {
        var dual = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            dual.DualMode = true;
            dual.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
            return dual;
        }
        catch (SocketException)
        {
            dual.Dispose();
        }
    }
    var ipv4 = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    ipv4.Bind(new IPEndPoint(IPAddress.Any, port));
    return ipv4;
}