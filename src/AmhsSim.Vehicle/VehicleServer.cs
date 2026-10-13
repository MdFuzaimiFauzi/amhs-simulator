using System.Net;
using System.Net.Sockets;
using AmhsSim.Protocol;

namespace AmhsSim.Vehicle;

/// <summary>
/// TCP front door for one vehicle. Accepts one controller at a time (like a real vehicle),
/// answers each request, and pushes Status (tx 0) when the vehicle's state changes.
/// </summary>
public sealed class VehicleServer(SimulatedVehicle vehicle, int port, ILogger logger)
{
    private CancellationTokenSource? _clientCts;

    public SimulatedVehicle Vehicle => vehicle;
    public int Port => port;

    /// <summary>Fault: close the current connection as if the cable was pulled.</summary>
    public void DropConnection() => _clientCts?.Cancel();

    public async Task RunAsync(CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        logger.LogInformation("{Vehicle} listening on port {Port}", vehicle.Name, port);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                using TcpClient client = await listener.AcceptTcpClientAsync(ct);
                logger.LogInformation("{Vehicle} controller connected from {Remote}", vehicle.Name, client.Client.RemoteEndPoint);
                await ServeClientAsync(client, ct);
                logger.LogInformation("{Vehicle} controller disconnected", vehicle.Name);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            listener.Stop();
        }
    }

    private async Task ServeClientAsync(TcpClient client, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _clientCts = cts;
        client.NoDelay = true;
        NetworkStream stream = client.GetStream();
        var writeLock = new SemaphoreSlim(1, 1); // replies and pushes must not interleave on the socket

        async Task WriteAsync(Frame frame)
        {
            byte[] bytes = FrameCodec.Encode(frame);
            if (vehicle.Faults.CorruptNextReply)
            {
                vehicle.Faults.CorruptNextReply = false;
                bytes[^1] ^= 0xFF;
            }
            await writeLock.WaitAsync(cts.Token);
            try { await stream.WriteAsync(bytes, cts.Token); }
            finally { writeLock.Release(); }
        }

        void OnStatusChanged(VehicleStatus status) =>
            _ = WriteAsync(new Frame(MessageType.Status, 0, status.ToPayload()))
                .ContinueWith(t => logger.LogDebug(t.Exception, "push failed"), TaskContinuationOptions.OnlyOnFaulted);

        vehicle.StatusChanged += OnStatusChanged;
        try
        {
            await foreach (Frame request in FrameReader.ReadFramesAsync(stream, cts.Token))
            {
                if (vehicle.Faults.Hang) continue; // swallow the request: the controller must time out
                Frame reply = vehicle.Handle(request);
                if (vehicle.Faults.LatencyMs > 0) await Task.Delay(vehicle.Faults.LatencyMs, cts.Token);
                await WriteAsync(reply);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException ex) { logger.LogWarning("{Vehicle} socket error: {Message}", vehicle.Name, ex.Message); }
        finally
        {
            vehicle.StatusChanged -= OnStatusChanged;
            _clientCts = null;
        }
    }
}
