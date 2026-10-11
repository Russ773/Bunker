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

    public event Action<RconMessage>? MessageReceived;
    public event Action<string>? Disconnected;

    public bool IsConnected => _ws?.State == WebSocketState.Open;

    public async Task ConnectAsync(string host, int port, string password)
    {
        await DisconnectAsync();
        _cts = new CancellationTokenSource();
        _ws = new ClientWebSocket();
        var uri = new Uri($"ws://{host}:{port}/{Uri.EscapeDataString(password)}");
        await _ws.ConnectAsync(uri, _cts.Token);
        _ = ReceiveLoop(_ws, _cts.Token);
    }

    public async Task SendCommandAsync(string command)
    {
        if (_ws is not { State: WebSocketState.Open }) throw new InvalidOperationException("Not connected");
        var json = JsonSerializer.Serialize(new { Identifier = Interlocked.Increment(ref _nextId), Message = command, Name = "Bunker" });
        await _ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, _cts!.Token);
    }

    public async Task DisconnectAsync()
    {
        var ws = _ws;
        _ws = null;
        _cts?.Cancel();
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
                    MessageReceived?.Invoke(new RconMessage(
                        root.TryGetProperty("Identifier", out var id) ? id.GetInt32() : 0,
                        root.TryGetProperty("Message", out var m) ? m.GetString() ?? "" : "",
                        root.TryGetProperty("Type", out var t) ? t.GetString() ?? "" : ""));
                }
                catch (JsonException) { }
            }
        }
        catch (OperationCanceledException) { reason = "Disconnected"; }
        catch (Exception e) { reason = "Connection lost: " + e.Message; }
        finally
        {
            if (!ct.IsCancellationRequested) Disconnected?.Invoke(reason);
        }
    }

    public void Dispose() => _ = DisconnectAsync();
}
