using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace FfxivVR;

public enum PoseSyncStatus
{
    Disconnected,
    Connecting,
    Connected,
    Unreachable,
    InvalidAddress,
}

public class PoseSyncClient(Logger logger) : IDisposable
{
    private const int MaxQueuedMessages = 1024;
    private static readonly TimeSpan WelcomeTimeout = TimeSpan.FromSeconds(5);

    public readonly ConcurrentQueue<PoseMessage> Incoming = new();

    private readonly object gate = new();
    private Socket? socket;
    private CancellationTokenSource? cancellation;
    private string? address;
    private long lastWelcome;
    private long token;
    private volatile bool unreachable;
    private volatile bool invalidAddress;

    public ulong Token => (ulong)Interlocked.Read(ref token);

    public PoseSyncStatus Status
    {
        get
        {
            lock (gate)
            {
                if (address == null)
                {
                    return PoseSyncStatus.Disconnected;
                }
                if (invalidAddress)
                {
                    return PoseSyncStatus.InvalidAddress;
                }
                if (Stopwatch.GetElapsedTime(Interlocked.Read(ref lastWelcome)) < WelcomeTimeout)
                {
                    return PoseSyncStatus.Connected;
                }
                return unreachable ? PoseSyncStatus.Unreachable : PoseSyncStatus.Connecting;
            }
        }
    }

    public static (string host, int port)? ParseAddress(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }
        if (Uri.TryCreate($"udp://{trimmed}", UriKind.Absolute, out var uri) && uri.Host.Length > 0 && uri.AbsolutePath == "/")
        {
            return (uri.IdnHost.Trim('[', ']'), uri.IsDefaultPort ? SyncProtocol.DefaultPort : uri.Port);
        }
        return null;
    }

    public void Connect(string serverAddress)
    {
        lock (gate)
        {
            if (address == serverAddress)
            {
                return;
            }
            DisconnectLocked();
            address = serverAddress;
            invalidAddress = false;
            unreachable = false;
            if (ParseAddress(serverAddress) is not var (host, port))
            {
                invalidAddress = true;
                return;
            }
            var cts = new CancellationTokenSource();
            cancellation = cts;
            _ = Task.Run(() => RunAsync(host, port, cts.Token));
        }
    }

    public void Disconnect()
    {
        lock (gate)
        {
            DisconnectLocked();
        }
    }

    private void DisconnectLocked()
    {
        cancellation?.Cancel();
        cancellation?.Dispose();
        cancellation = null;
        socket?.Dispose();
        socket = null;
        address = null;
        Interlocked.Exchange(ref token, 0);
        Interlocked.Exchange(ref lastWelcome, 0);
        Incoming.Clear();
    }

    public void Send(ReadOnlySpan<byte> packet)
    {
        Socket? current;
        lock (gate)
        {
            current = socket;
        }
        if (current == null)
        {
            return;
        }
        try
        {
            current.Send(packet);
        }
        catch (SocketException)
        {
            unreachable = true;
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task RunAsync(string host, int port, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
                var target = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                    ?? addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetworkV6);
                if (target != null)
                {
                    invalidAddress = false;
                    var created = new Socket(target.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
                    created.Connect(new IPEndPoint(target, port));
                    lock (gate)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            created.Dispose();
                            return;
                        }
                        socket = created;
                    }
                    logger.Debug($"Pose sync connecting to {host}:{port}");
                    await ReceiveLoopAsync(created, cancellationToken);
                    return;
                }
                invalidAddress = true;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException e)
            {
                unreachable = true;
                invalidAddress = e.SocketErrorCode == SocketError.HostNotFound;
                logger.Debug($"Pose sync failed to resolve {host}: {e.Message}");
            }
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ReceiveLoopAsync(Socket receiveSocket, CancellationToken cancellationToken)
    {
        var buffer = new byte[SyncProtocol.MaxPacketSize];
        while (!cancellationToken.IsCancellationRequested)
        {
            int length;
            try
            {
                length = await receiveSocket.ReceiveAsync(buffer, SocketFlags.None, cancellationToken);
            }
            catch (SocketException)
            {
                unreachable = true;
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                continue;
            }
            unreachable = false;
            var data = buffer.AsSpan(0, length);
            if (SyncProtocol.TryReadWelcome(data, out var welcomeToken))
            {
                Interlocked.Exchange(ref token, (long)welcomeToken);
                Interlocked.Exchange(ref lastWelcome, Stopwatch.GetTimestamp());
            }
            else if (SyncProtocol.TryReadPose(data, out var message) && message != null && Incoming.Count < MaxQueuedMessages)
            {
                Incoming.Enqueue(message);
            }
        }
    }

    public void Dispose()
    {
        Disconnect();
    }
}