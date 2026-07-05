using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;

namespace Overlay;

/// <summary>
/// The .NET equivalent of <c>src/websocket-hub.js</c> plus the single-comment state that
/// <c>src/server.js</c> held in <c>currentComment</c>. Kestrel's <c>UseWebSockets()</c> already
/// performs the RFC 6455 handshake and frame encoding, so this class only has to track the open
/// sockets, broadcast JSON text frames, and hand each new client the current comment as an
/// <c>init</c> message. Sends on a single socket are serialized with a per-socket lock because
/// <see cref="WebSocket.SendAsync"/> forbids concurrent writes; state access is guarded so the
/// HTTP threads (which Kestrel runs concurrently, unlike single-threaded Node) stay consistent.
/// </summary>
public sealed class OverlayHub
{
    private sealed class Client
    {
        public required WebSocket Socket { get; init; }

        public SemaphoreSlim SendLock { get; } = new(1, 1);
    }

    private readonly ConcurrentDictionary<Guid, Client> _clients = new();
    private readonly Lock _stateLock = new();
    private readonly JsonSerializerOptions _json;
    private Comment? _current;

    public OverlayHub(JsonSerializerOptions json) => _json = json;

    public Comment? Current
    {
        get
        {
            lock (_stateLock)
            {
                return _current;
            }
        }
    }

    /// <summary>Stores the comment and broadcasts a <c>show</c> message (POST /api/comments/show).</summary>
    public async Task ShowAsync(Comment comment)
    {
        lock (_stateLock)
        {
            _current = comment;
        }

        await BroadcastAsync(new { type = "show", comment });
    }

    /// <summary>Clears the comment and broadcasts a <c>clear</c> message (POST /api/comments/clear).</summary>
    public async Task ClearAsync()
    {
        lock (_stateLock)
        {
            _current = null;
        }

        await BroadcastAsync(new { type = "clear" });
    }

    /// <summary>
    /// Registers an accepted socket, sends it the <c>init</c> message, then reads until the client
    /// closes. Incoming data frames are ignored (the Node hub only reacts to ping and close; Kestrel
    /// answers pings for us automatically).
    /// </summary>
    public async Task HandleClientAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var client = new Client { Socket = socket };
        _clients[id] = client;

        try
        {
            Comment? snapshot;
            lock (_stateLock)
            {
                snapshot = _current;
            }

            await SendPayloadAsync(client, new { type = "init", comment = snapshot });

            var buffer = new byte[4096];
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken);
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The connection was aborted (client gone / server shutting down).
        }
        catch (WebSocketException)
        {
            // The socket faulted; just drop it.
        }
        finally
        {
            _clients.TryRemove(id, out _);
        }
    }

    private Task BroadcastAsync(object payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, _json);
        var sends = new List<Task>();
        foreach (var client in _clients.Values)
        {
            sends.Add(SendBytesAsync(client, bytes));
        }

        return Task.WhenAll(sends);
    }

    private Task SendPayloadAsync(Client client, object payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, _json);
        return SendBytesAsync(client, bytes);
    }

    private static async Task SendBytesAsync(Client client, byte[] bytes)
    {
        if (client.Socket.State != WebSocketState.Open)
        {
            return;
        }

        await client.SendLock.WaitAsync();
        try
        {
            if (client.Socket.State == WebSocketState.Open)
            {
                await client.Socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
            }
        }
        catch (WebSocketException)
        {
            // Client vanished mid-send; the receive loop will remove it.
        }
        catch (ObjectDisposedException)
        {
            // Socket already disposed; ignore.
        }
        finally
        {
            client.SendLock.Release();
        }
    }
}
