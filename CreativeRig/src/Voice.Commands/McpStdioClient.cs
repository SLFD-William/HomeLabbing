using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Voice.Commands;

/// <summary>Minimal newline-delimited JSON-RPC client for one of the Mcp.* stdio servers.</summary>
public sealed class McpStdioClient : IAsyncDisposable
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private readonly string _exePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private int _nextId;

    public McpStdioClient(string exePath)
    {
        _exePath = exePath;
    }

    private async Task EnsureStartedAsync(CancellationToken ct)
    {
        if (_process is { HasExited: false })
        {
            return;
        }

        _process?.Dispose();
        _process = Process.Start(new ProcessStartInfo(_exePath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException($"Could not start {_exePath}");

        _process.ErrorDataReceived += (_, _) => { };
        _process.BeginErrorReadLine();

        await RequestAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = "2024-11-05",
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "Voice.Commands", ["version"] = "1.0.0" },
        }, ct).ConfigureAwait(false);

        await _process.StandardInput.WriteLineAsync(
            new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" }.ToJsonString()).ConfigureAwait(false);
        await _process.StandardInput.FlushAsync(ct).ConfigureAwait(false);
    }

    private async Task<JsonNode> RequestAsync(string method, JsonObject parameters, CancellationToken ct)
    {
        var id = Interlocked.Increment(ref _nextId);
        var proc = _process!;
        await proc.StandardInput.WriteLineAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters,
        }.ToJsonString()).ConfigureAwait(false);
        await proc.StandardInput.FlushAsync(ct).ConfigureAwait(false);

        while (true)
        {
            var line = await proc.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"{Path.GetFileName(_exePath)} closed its output.");
            if (JsonNode.Parse(line) is not JsonObject msg || msg["id"]?.GetValue<int>() != id)
            {
                continue;
            }

            if (msg["error"] is JsonNode err)
            {
                throw new InvalidOperationException(err["message"]?.GetValue<string>() ?? "MCP error");
            }

            return msg["result"] ?? new JsonObject();
        }
    }

    /// <summary>Calls an MCP tool and returns its text content; throws when the tool reports an error.</summary>
    public async Task<string> CallToolAsync(string tool, JsonObject? arguments = null, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? DefaultTimeout);
        try
        {
            await EnsureStartedAsync(cts.Token).ConfigureAwait(false);
            var result = await RequestAsync("tools/call", new JsonObject
            {
                ["name"] = tool,
                ["arguments"] = arguments ?? new JsonObject(),
            }, cts.Token).ConfigureAwait(false);

            var text = result["content"]?[0]?["text"]?.GetValue<string>() ?? "";
            if (result["isError"]?.GetValue<bool>() == true)
            {
                throw new InvalidOperationException(text);
            }

            return text;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // The server may still answer later; restart it so replies can't get out of step.
            Kill();
            throw new TimeoutException($"{tool} timed out.");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Returns the server's tool definitions (name, description, inputSchema).</summary>
    public async Task<JsonArray> ListToolsAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(DefaultTimeout);
        try
        {
            await EnsureStartedAsync(cts.Token).ConfigureAwait(false);
            var result = await RequestAsync("tools/list", new JsonObject(), cts.Token).ConfigureAwait(false);
            return result["tools"]?.AsArray() ?? [];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<JsonNode?> CallJsonAsync(string tool, JsonObject? arguments = null, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var text = await CallToolAsync(tool, arguments, timeout, ct).ConfigureAwait(false);
        try
        {
            return JsonNode.Parse(text);
        }
        catch (System.Text.Json.JsonException)
        {
            return JsonValue.Create(text);
        }
    }

    public Task<string> CallReaScriptAsync(string function, JsonArray args, CancellationToken ct = default) =>
        CallToolAsync("call_reascript_function", new JsonObject
        {
            ["functionName"] = function,
            ["args"] = args,
        }, ct: ct);

    private void Kill()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_process is { HasExited: false })
        {
            try
            {
                _process.StandardInput.Close();
                if (!_process.WaitForExit(2000))
                {
                    _process.Kill(true);
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        _process?.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>Lazily-started clients for each Rig MCP server, located via the Copilot MCP config.</summary>
public sealed class McpHub : IAsyncDisposable
{
    private readonly Dictionary<string, McpStdioClient> _clients = new();

    public McpStdioClient Reaper => Get("RigReaper");

    public McpStdioClient Obs => Get("RigObs");

    public McpStdioClient Headrush => Get("RigHeadrush");

    public McpStdioClient CapCut => Get("RigCapCut");

    public McpStdioClient ByName(string server) => Get(server);

    private McpStdioClient Get(string server)
    {
        lock (_clients)
        {
            if (!_clients.TryGetValue(server, out var client))
            {
                _clients[server] = client = new McpStdioClient(ResolveExe(server));
            }

            return client;
        }
    }

    private static string ResolveExe(string server)
    {
        var overridePath = Environment.GetEnvironmentVariable($"{server.ToUpperInvariant()}_EXE");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return overridePath;
        }

        var config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot", "mcp-config.json");
        try
        {
            if (File.Exists(config)
                && JsonNode.Parse(File.ReadAllText(config))?["mcpServers"]?[server]?["command"]?.GetValue<string>() is { Length: > 0 } cmd)
            {
                return cmd;
            }
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException)
        {
        }

        var short_ = server.Replace("Rig", "Mcp.");
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Repository", "CreativeRig", "publish", short_, short_ + ".exe");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var c in _clients.Values)
        {
            await c.DisposeAsync().ConfigureAwait(false);
        }
    }
}
