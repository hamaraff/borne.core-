using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;

namespace borne.central.api;

public sealed class CentralRealtimeHub
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<Guid, MonitorPeer> clients = new();

    public MonitorPeer Add(WebSocket socket)
    {
        var peer = new MonitorPeer(Guid.NewGuid(), socket);
        clients[peer.Id] = peer;
        return peer;
    }

    public void Remove(MonitorPeer peer) => clients.TryRemove(peer.Id, out _);

    public Task SendAsync(MonitorPeer peer, string eventName, object data, CancellationToken cancellationToken)
    {
        byte[] frame = JsonSerializer.SerializeToUtf8Bytes(new
        {
            @event = eventName, version = 1, messageId = Guid.NewGuid(), sentAtUtc = DateTimeOffset.UtcNow, data
        }, JsonOptions);
        return peer.SendAsync(frame, cancellationToken);
    }

    public async Task BroadcastAsync(string eventName, object data, CancellationToken cancellationToken)
    {
        Task[] sends = clients.Values.Select(peer => SendBestEffortAsync(peer, eventName, data, cancellationToken)).ToArray();
        await Task.WhenAll(sends);
    }

    private async Task SendBestEffortAsync(MonitorPeer peer, string eventName, object data, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            await SendAsync(peer, eventName, data, timeout.Token);
        }
        catch (WebSocketException) { Remove(peer); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { Remove(peer); }
    }

    public sealed class MonitorPeer(Guid id, WebSocket socket)
    {
        private readonly SemaphoreSlim sendLock = new(1, 1);
        public Guid Id { get; } = id;
        public WebSocket Socket { get; } = socket;
        public async Task SendAsync(byte[] frame, CancellationToken cancellationToken)
        {
            await sendLock.WaitAsync(cancellationToken);
            try { if (Socket.State == WebSocketState.Open) await Socket.SendAsync(frame, WebSocketMessageType.Text, true, cancellationToken); }
            finally { sendLock.Release(); }
        }
    }
}
