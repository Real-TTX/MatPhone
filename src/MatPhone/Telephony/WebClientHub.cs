using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace MatPhone.Telephony;

/// <summary>One open web client: the WebSocket of a page of a signed-in person. Messages go out through a queue (one writer), in order.</summary>
public sealed class WebClientConnection(WebSocket socket, long userId, string name)
{
    private readonly Channel<string> _outbox = Channel.CreateBounded<string>(new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropOldest });

    public string Id { get; } = Guid.NewGuid().ToString("N")[..10];

    public long UserId { get; } = userId;

    public string Name { get; } = name;

    public WebSocket Socket { get; } = socket;

    /// <summary>The page ringing for calls (a page that only shows the panel does not ring: the person opened the phone elsewhere).</summary>
    public bool CanRing { get; set; } = true;

    public void Send(JsonObject message) => _outbox.Writer.TryWrite(message.ToJsonString());

    public void Complete() => _outbox.Writer.TryComplete();

    public async Task SendLoopAsync(CancellationToken ct)
    {
        await foreach (var text in _outbox.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            if (Socket.State != WebSocketState.Open)
            {
                return;
            }

            await Socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// The WebSocket endpoint of the web client (<c>/ws/phone</c>): keeps the open connections per person, reads their messages and hands them to the engine, and
/// sends what the engine says (calls, the state of colleagues, the person's own state).
/// </summary>
public sealed class WebClientHub(ILogger<WebClientHub> logger)
{
    private readonly ConcurrentDictionary<string, WebClientConnection> _connections = new();

    /// <summary>A message of a browser: the engine acts on it.</summary>
    public Func<WebClientConnection, JsonObject, Task>? OnMessage { get; set; }

    /// <summary>A connection opened or closed: the presence of the person changes.</summary>
    public event Action<WebClientConnection, bool>? ConnectionChanged;

    public IEnumerable<WebClientConnection> Of(long userId) => _connections.Values.Where(c => c.UserId == userId);

    public bool IsOnline(long userId) => _connections.Values.Any(c => c.UserId == userId);

    public int Count => _connections.Count;

    public void Broadcast(JsonObject message)
    {
        foreach (var connection in _connections.Values)
        {
            connection.Send((JsonObject)message.DeepClone());
        }
    }

    public void SendTo(long userId, JsonObject message)
    {
        foreach (var connection in Of(userId))
        {
            connection.Send((JsonObject)message.DeepClone());
        }
    }

    public async Task RunAsync(WebSocket socket, long userId, string name, CancellationToken ct)
    {
        var connection = new WebClientConnection(socket, userId, name);
        _connections[connection.Id] = connection;
        ConnectionChanged?.Invoke(connection, true);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var sending = connection.SendLoopAsync(stop.Token);
        try
        {
            var buffer = new byte[16 * 1024];
            var message = new MemoryStream();
            while (socket.State == WebSocketState.Open && !stop.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(buffer, stop.Token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                message.Write(buffer, 0, result.Count);
                if (message.Length > 256 * 1024)
                {
                    // Nothing the client sends is this large: a page that does is not ours.
                    break;
                }

                if (!result.EndOfMessage)
                {
                    continue;
                }

                var text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                message.SetLength(0);
                if (JsonNode.Parse(text) is JsonObject json && OnMessage is { } handler)
                {
                    try
                    {
                        await handler(connection, json).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "A message of the web client could not be handled.");
                        connection.Send(new JsonObject { ["t"] = "error", ["message"] = "internal" });
                    }
                }
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or System.Text.Json.JsonException)
        {
        }
        finally
        {
            _connections.TryRemove(connection.Id, out _);
            connection.Complete();
            await stop.CancelAsync().ConfigureAwait(false);
            try
            {
                await sending.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
            {
            }

            ConnectionChanged?.Invoke(connection, false);
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None).ConfigureAwait(false);
                }
                catch (WebSocketException)
                {
                }
            }
        }
    }
}
