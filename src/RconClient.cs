using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Bunker;

public record RconMessage(int Identifier, string Message, string Type);

/// <summary>Minimal Rust WebRCON client (ws://host:port/password, JSON messages).</summary>
public sealed class RconClient : IDisposable
{
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private int _nextId = 1000;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<RconMessage>> _pending = new();

    /// <summary>Fires for console output and replies to commands sent with SendCommandAsync (not QueryAsync).</summary>
    public event Action<RconMessage>? MessageReceived;
    public event Action<string>? Disconnected;

    public bool IsConnected => _ws?.State == WebSocketState.Open;

    public async Task ConnectAsync(string host, int port, string password, CancellationToken ct = default)
    {
        await DisconnectAsync();
        _cts = new CancellationTokenSource();
        _ws = new ClientWebSocket();
        var uri = new Uri($"ws://{host}:{port}/{Uri.EscapeDataString(password)}");
        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, ct))
            await _ws.ConnectAsync(uri, linked.Token);
        _ = ReceiveLoop(_ws, _cts.Token);
    }

    /// <summary>Sends a command; the reply is delivered through MessageReceived.</summary>
    public async Task SendCommandAsync(string command) => await SendRawAsync(Interlocked.Increment(ref _nextId), command);

    /// <summary>Sends a command and returns its reply without it appearing in the console.</summary>
    public async Task<string> QueryAsync(string command, int timeoutMs = 5000)
    {
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<RconMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        try
        {
            await SendRawAsync(id, command);
            using var timeout = new CancellationTokenSource(timeoutMs);
            using var reg = timeout.Token.Register(() => tcs.TrySetCanceled());
            return (await tcs.Task).Message;
        }
        finally { _pending.TryRemove(id, out _); }
    }

    private async Task SendRawAsync(int id, string command)
    {
        if (_ws is not { State: WebSocketState.Open }) throw new InvalidOperationException("Not connected");
        var json = JsonSerializer.Serialize(new { Identifier = id, Message = command, Name = "Bunker" });
        await _ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, _cts!.Token);
    }

    public async Task DisconnectAsync()
    {
        var ws = _ws;
        _ws = null;
        _cts?.Cancel();
        foreach (var p in _pending.Values) p.TrySetCanceled();
        if (ws is { State: WebSocketState.Open })
        {
            try { await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch { }
        }
        ws?.Dispose();
    }

    private async Task ReceiveLoop(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        string reason = "Disconnected";
        try
        {
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult r;
                do
                {
                    r = await ws.ReceiveAsync(buffer, ct);
                    if (r.MessageType == WebSocketMessageType.Close) { reason = "Server closed the connection"; return; }
                    ms.Write(buffer, 0, r.Count);
                } while (!r.EndOfMessage);

                try
                {
                    using var doc = JsonDocument.Parse(ms.ToArray());
                    var root = doc.RootElement;
                    var msg = new RconMessage(
                        root.TryGetProperty("Identifier", out var id) ? id.GetInt32() : 0,
                        root.TryGetProperty("Message", out var m) ? m.GetString() ?? "" : "",
                        root.TryGetProperty("Type", out var t) ? t.GetString() ?? "" : "");
                    if (_pending.TryGetValue(msg.Identifier, out var tcs)) tcs.TrySetResult(msg);
                    else MessageReceived?.Invoke(msg);
                }
                catch (JsonException) { }
            }
        }
        catch (OperationCanceledException) { reason = "Disconnected"; }
        catch (Exception e) { reason = "Connection lost: " + e.Message; }
        finally
        {
            foreach (var p in _pending.Values) p.TrySetCanceled();
            if (!ct.IsCancellationRequested) Disconnected?.Invoke(reason);
        }
    }

    public void Dispose() => _ = DisconnectAsync();
}
