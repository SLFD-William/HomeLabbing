using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mcp.Core;

/// <summary>
/// Base class for a stdio-transport MCP server. Each line of stdin is a
/// single-line JSON <see cref="McpRequest"/>; each response is written back
/// as a single-line JSON <see cref="McpResponse"/> on stdout.
///
/// This mirrors the simple newline-delimited-JSON transport used by the
/// design document's MCP servers (Reaper/Headrush/OBS/CapCut), rather than
/// implementing the full MCP wire protocol directly - swap this out for a
/// real MCP SDK transport once one is wired into the harness config.
/// </summary>
public abstract class McpServerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Returns the set of tools this server exposes.</summary>
    protected abstract IEnumerable<McpToolDescriptor> GetTools();

    /// <summary>Executes a single tool invocation and returns its result.</summary>
    protected abstract Task<McpResponse> ExecuteAsync(McpRequest request, CancellationToken ct);

    protected virtual string ServerName => GetType().Name;

    /// <summary>Handles one MCP (JSON-RPC 2.0) message; returns null for notifications.</summary>
    private async Task<string?> HandleJsonRpcAsync(string line, CancellationToken ct)
    {
        JsonNode? id = null;
        try
        {
            var msg = JsonNode.Parse(line) as JsonObject ?? throw new InvalidOperationException("Not an object.");
            id = msg["id"]?.DeepClone();
            var method = msg["method"]?.GetValue<string>();
            if (id is null)
            {
                return null; // notification (e.g. notifications/initialized)
            }

            JsonNode result;
            switch (method)
            {
                case "initialize":
                    result = new JsonObject
                    {
                        ["protocolVersion"] = msg["params"]?["protocolVersion"]?.GetValue<string>() ?? "2024-11-05",
                        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                        ["serverInfo"] = new JsonObject { ["name"] = ServerName, ["version"] = "1.0.0" },
                    };
                    break;
                case "ping":
                    result = new JsonObject();
                    break;
                case "tools/list":
                    var arr = new JsonArray();
                    foreach (var t in GetTools())
                    {
                        arr.Add(new JsonObject
                        {
                            ["name"] = t.Name,
                            ["description"] = t.Description,
                            ["inputSchema"] = JsonNode.Parse(t.InputSchema),
                        });
                    }

                    result = new JsonObject { ["tools"] = arr };
                    break;
                case "tools/call":
                    var name = msg["params"]?["name"]?.GetValue<string>() ?? "";
                    var args = msg["params"]?["arguments"]?.ToJsonString() ?? "{}";
                    var resp = await ExecuteAsync(
                        new McpRequest { Id = id.ToJsonString(), Tool = name, ArgumentsJson = args }, ct).ConfigureAwait(false);
                    result = new JsonObject
                    {
                        ["content"] = new JsonArray(new JsonObject
                        {
                            ["type"] = "text",
                            ["text"] = resp.Success ? resp.ResultJson ?? "null" : resp.Error ?? "error",
                        }),
                        ["isError"] = !resp.Success,
                    };
                    break;
                default:
                    return new JsonObject
                    {
                        ["jsonrpc"] = "2.0",
                        ["id"] = id,
                        ["error"] = new JsonObject { ["code"] = -32601, ["message"] = $"Method not found: {method}" },
                    }.ToJsonString();
            }

            return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result }.ToJsonString();
        }
        catch (Exception ex)
        {
            return new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["error"] = new JsonObject { ["code"] = -32603, ["message"] = ex.Message },
            }.ToJsonString();
        }
    }

    /// <summary>
    /// Runs the read-eval-respond loop against stdin/stdout until stdin is
    /// closed or cancellation is requested.
    /// </summary>
    public async Task RunStdioAsync(CancellationToken ct = default)
    {
        using var stdin = Console.OpenStandardInput();
        using var stdout = Console.OpenStandardOutput();
        using var reader = new StreamReader(stdin);
        await using var writer = new StreamWriter(stdout) { AutoFlush = true };

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null)
            {
                break; // stdin closed
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.Contains("\"jsonrpc\"", StringComparison.Ordinal))
            {
                var rpc = await HandleJsonRpcAsync(line, ct).ConfigureAwait(false);
                if (rpc is not null)
                {
                    await writer.WriteLineAsync(rpc).ConfigureAwait(false);
                }

                continue;
            }

            McpResponse response;
            string requestId = "unknown";
            try
            {
                var request = JsonSerializer.Deserialize<McpRequest>(line, JsonOptions)
                    ?? throw new InvalidOperationException("Request deserialized to null.");
                requestId = request.Id;

                if (string.Equals(request.Tool, "__list_tools__", StringComparison.Ordinal))
                {
                    var tools = GetTools();
                    response = McpResponse.Ok(request.Id, JsonSerializer.Serialize(tools));
                }
                else
                {
                    response = await ExecuteAsync(request, ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                response = McpResponse.Fail(requestId, ex.Message);
            }

            var json = JsonSerializer.Serialize(response);
            await writer.WriteLineAsync(json).ConfigureAwait(false);
        }
    }
}
