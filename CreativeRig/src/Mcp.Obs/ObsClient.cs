using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Mcp.Obs;

/// <summary>
/// Minimal obs-websocket v5 client (protocol.md): Hello/Identify handshake with
/// SHA256 authentication, then request/response calls. Connects lazily and
/// reconnects if the socket has dropped.
/// </summary>
public sealed class ObsClient : IDisposable
{
    private readonly Uri _uri;
    private readonly string? _password;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private ClientWebSocket? _socket;

    public ObsClient(string url, string? password)
    {
        _uri = new Uri(url);
        _password = password;
    }

    public async Task<JsonNode?> CallAsync(
        string requestType, JsonNode? requestData, CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_socket is not { State: WebSocketState.Open })
            {
                await ConnectAsync(ct).ConfigureAwait(false);
            }

            var requestId = Guid.NewGuid().ToString("N");
            var message = new JsonObject
            {
                ["op"] = 6,
                ["d"] = new JsonObject
                {
                    ["requestType"] = requestType,
                    ["requestId"] = requestId,
                    ["requestData"] = requestData?.DeepClone(),
                },
            };
            await SendAsync(message, ct).ConfigureAwait(false);

            // Skip events (op 5) until our matching RequestResponse (op 7) arrives.
            while (true)
            {
                var reply = await ReceiveAsync(ct).ConfigureAwait(false);
                if (reply["op"]?.GetValue<int>() != 7 ||
                    reply["d"]?["requestId"]?.GetValue<string>() != requestId)
                {
                    continue;
                }

                var data = reply["d"]!;
                var status = data["requestStatus"];
                if (status?["result"]?.GetValue<bool>() != true)
                {
                    var code = status?["code"]?.ToString();
                    var comment = status?["comment"]?.GetValue<string>();
                    throw new InvalidOperationException(
                        $"OBS request '{requestType}' failed (code {code}): {comment}");
                }

                return data["responseData"]?.DeepClone();
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        _socket?.Dispose();
        _socket = new ClientWebSocket();
        _socket.Options.AddSubProtocol("obswebsocket.json");
        await _socket.ConnectAsync(_uri, ct).ConfigureAwait(false);

        var hello = await ReceiveAsync(ct).ConfigureAwait(false);
        var helloData = hello["d"]!;

        var identify = new JsonObject
        {
            ["rpcVersion"] = 1,
            // 0 = no event subscriptions; we only use request/response.
            ["eventSubscriptions"] = 0,
        };

        if (helloData["authentication"] is JsonObject auth)
        {
            if (string.IsNullOrEmpty(_password))
            {
                throw new InvalidOperationException(
                    "OBS requires a password. Set OBS_WS_PASSWORD or enable the WebSocket server password in OBS.");
            }

            identify["authentication"] = ComputeAuth(
                _password, auth["salt"]!.GetValue<string>(), auth["challenge"]!.GetValue<string>());
        }

        await SendAsync(new JsonObject { ["op"] = 1, ["d"] = identify }, ct).ConfigureAwait(false);

        var identified = await ReceiveAsync(ct).ConfigureAwait(false);
        if (identified["op"]?.GetValue<int>() != 2)
        {
            throw new InvalidOperationException("OBS did not accept the Identify message (wrong password?).");
        }
    }

    private static string ComputeAuth(string password, string salt, string challenge)
    {
        var secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }

    private async Task SendAsync(JsonNode message, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(message.ToJsonString());
        await _socket!.SendAsync(bytes, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
    }

    private async Task<JsonNode> ReceiveAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        var buffer = new byte[16 * 1024];
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await _socket!.ReceiveAsync(buffer, timeout.Token).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new InvalidOperationException(
                    $"OBS closed the connection: {result.CloseStatusDescription}");
            }

            stream.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return JsonNode.Parse(stream.ToArray())
            ?? throw new InvalidOperationException("OBS sent an empty message.");
    }

    public void Dispose()
    {
        _socket?.Dispose();
        _lock.Dispose();
    }
}
