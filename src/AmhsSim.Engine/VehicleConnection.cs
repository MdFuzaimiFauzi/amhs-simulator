using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using AmhsSim.Protocol;

namespace AmhsSim.Engine;

public enum ConnectionState { Disconnected, Connecting, Connected, Online }

/// <summary>
/// The controller's TCP client for one vehicle: connect, heartbeat, match replies to requests,
/// and reconnect with backoff when anything goes wrong.
/// </summary>
public sealed class VehicleConnection(string name, string host, int port, ILogger logger, ILogger frameLog)
{
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _sendLock = new(1, 1); // limit number of threads that can access a shared resource
    private readonly ConcurrentDictionary<byte, TaskCompletionSource<Frame>> _pending = new();
    private NetworkStream? _stream;
    private int _txCounter;

    public string Name => name;
    public string Endpoint => $"{host}:{port}";
    public ConnectionState State { get; private set; }
    public VehicleStatus? Status { get; private set; }
    public double RoundTripMs { get; private set; }
    public DateTime? LastReplyAt { get; private set; }
    public bool IsOnline => State == ConnectionState.Online;

    /// <summary>Raised on every status frame and every connection state change.</summary>
    public event Action<VehicleConnection>? Changed;

    public async Task RunAsync(CancellationToken ct)
    {
        TimeSpan backoff = TimeSpan.FromSeconds(1);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                SetState(ConnectionState.Connecting);
                using var client = new TcpClient { NoDelay = true };
                await client.ConnectAsync(host, port, ct);
                _stream = client.GetStream();
                SetState(ConnectionState.Connected);
                logger.LogInformation("{Vehicle} connected to {Endpoint}", name, Endpoint);
                backoff = TimeSpan.FromSeconds(1);

                using var session = CancellationTokenSource.CreateLinkedTokenSource(ct);
                Task reading = ReadLoopAsync(_stream, session.Token);
                Task heartbeat = HeartbeatLoopAsync(session.Token);

                // Whichever ends first (socket closed, or heartbeat timed out) ends the session.
                Task first = await Task.WhenAny(reading, heartbeat);
                session.Cancel();
                if (first.Exception?.GetBaseException() is { } ex && !ct.IsCancellationRequested)
                    logger.LogWarning("{Vehicle} link lost: {Reason}", name, ex.Message);
                else if (!ct.IsCancellationRequested)
                    logger.LogWarning("{Vehicle} link lost: connection closed by vehicle", name);
                await Task.WhenAll(reading, heartbeat).ContinueWith(_ => { }); // observe, don't rethrow
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning("{Vehicle} connect to {Endpoint} failed: {Reason}", name, Endpoint, ex.Message);
            }
            finally
            {
                _stream = null;
                FailPendingRequests();
                SetState(ConnectionState.Disconnected);
            }

            try { await Task.Delay(backoff, ct); }
            catch (OperationCanceledException) { break; }
            backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
        }
    }

    /// <summary>
    /// Sends one request and waits for the reply with the same transaction id.
    /// Throws TimeoutException when the vehicle does not answer in time.
    /// </summary>
    public async Task<Frame> SendAsync(MessageType type, byte[] payload, CancellationToken ct = default)
    {
        NetworkStream stream = _stream ?? throw new InvalidOperationException($"{name} is not connected");

        await _sendLock.WaitAsync(ct);
        try
        {
            byte tx = NextTxId();
            var reply = new TaskCompletionSource<Frame>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[tx] = reply;
            try
            {
                var request = new Frame(type, tx, payload);
                byte[] bytes = FrameCodec.Encode(request);
                frameLog.LogInformation("{Vehicle} TX {Hex}", name, Convert.ToHexString(bytes));
                await stream.WriteAsync(bytes, ct);

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(ReplyTimeout);
                try
                {
                    return await reply.Task.WaitAsync(timeout.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new TimeoutException($"{name}: no reply to {type} (tx {tx}) within {ReplyTimeout.TotalSeconds:0} s");
                }
            }
            finally
            {
                _pending.TryRemove(tx, out _);
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    // 1..255; 0 is reserved for frames the vehicle pushes on its own.
    private byte NextTxId() => (byte)(Interlocked.Increment(ref _txCounter) % 255 + 1);

    private async Task ReadLoopAsync(NetworkStream stream, CancellationToken ct)
    {
        await foreach (Frame frame in FrameReader.ReadFramesAsync(stream, ct))
        {
            frameLog.LogInformation("{Vehicle} RX {Hex}", name, Convert.ToHexString(FrameCodec.Encode(frame)));
            LastReplyAt = DateTime.UtcNow;

            if (frame.Type == MessageType.Status)
            {
                Status = VehicleStatus.FromPayload(frame.Payload);
                Changed?.Invoke(this);
            }

            if (frame.TxId != 0 && _pending.TryRemove(frame.TxId, out var waitEvent))
                waitEvent.TrySetResult(frame);
        }
        throw new IOException("Connection closed by vehicle");
    }

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var sw = Stopwatch.StartNew();
            await SendAsync(MessageType.Heartbeat, Payloads.Empty, ct); // throws on timeout -> session ends
            RoundTripMs = sw.Elapsed.TotalMilliseconds;
            if (State == ConnectionState.Connected) SetState(ConnectionState.Online);
            await Task.Delay(HeartbeatInterval, ct);
        }
    }

    private void FailPendingRequests()
    {
        foreach (var tx in _pending.Keys)
            if (_pending.TryRemove(tx, out var waitEvent))
                waitEvent.TrySetException(new IOException($"{name} connection lost"));
    }

    private void SetState(ConnectionState state)
    {
        if (State == state) return;
        State = state;
        Changed?.Invoke(this);
    }
}
