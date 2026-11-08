using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace borne.central.api;

/// <summary>Tracks authenticated Borne sockets and serializes writes to each socket.</summary>
public sealed class BorneRealtimeHub
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, BornePeer> peers = new(StringComparer.Ordinal);

    public BornePeer Add(string borneId, WebSocket socket)
    {
        var peer = new BornePeer(borneId, socket);
        peers.AddOrUpdate(borneId, peer, (_, previous) =>
        {
            previous.Close();
            return peer;
        });
        return peer;
    }

    public void Remove(BornePeer peer) =>
        ((ICollection<KeyValuePair<string, BornePeer>>)peers).Remove(new(peer.BorneId, peer));

    public async Task<int> BroadcastAsync(string eventName, object data, CancellationToken cancellationToken)
    {
        int sent = 0;
        foreach (BornePeer peer in peers.Values)
        {
            try
            {
                await SendAsync(peer.Socket, eventName, data, cancellationToken);
                sent++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is WebSocketException or OperationCanceledException)
            {
                Remove(peer);
                peer.Close();
            }
        }

        return sent;
    }

    public async Task SendAsync(WebSocket socket, string eventName, object data, CancellationToken cancellationToken)
    {
        if (socket.State != WebSocketState.Open) return;
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            @event = eventName,
            version = 1,
            messageId = Guid.NewGuid(),
            sentAtUtc = DateTimeOffset.UtcNow,
            data
        }, JsonOptions);
        SemaphoreSlim gate = gates.GetValue(socket, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (socket.State == WebSocketState.Open)
                await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private readonly ConditionalWeakTable<WebSocket, SemaphoreSlim> gates = new();

    public sealed class BornePeer(string borneId, WebSocket socket)
    {
        public string BorneId { get; } = borneId;
        public WebSocket Socket { get; } = socket;
        public void Close() => Socket.Abort();
    }
}
