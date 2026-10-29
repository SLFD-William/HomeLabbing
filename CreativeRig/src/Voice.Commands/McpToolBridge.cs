using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace Voice.Commands;

/// <summary>
/// Exposes the Rig MCP servers' own tools to the language model, so it can do anything the CLI could
/// (for example add a MIDI track through ReaScript), with confirmation required for risky calls.
/// </summary>
public static class McpToolBridge
{
    public const string NeedConfirm = "Not done. Ask the Captain to confirm first, then call again with confirmed=true.";

    private static readonly Regex ReadOnlyTool = new(@"^(list|get|read|analyze)_", RegexOptions.Compiled);
    private static readonly Regex ReadOnlyReaScript = new(@"^(Get|Count|Enum|Has|Is|Master_Get|Time|Format|Resolve|BR_Get)", RegexOptions.Compiled);
    private static readonly Regex RiskyReaScript = new(@"(Delete|Remove|Clear|Save|Render|Quit|Close|New|Open|Undo|Overwrite|Reset|Purge|Kill|Exit)", RegexOptions.Compiled);
    private static readonly Regex RiskyObs = new(@"(Remove|Delete|Stop|Start)(Stream|Output|Virtual)|Remove|Delete|Exit|Close", RegexOptions.Compiled);

    /// <summary>Server names (as in the Copilot MCP config) to expose; ELMER_MCP_SERVERS overrides.</summary>
    public static IReadOnlyList<string> ServerNames()
    {
        var env = Environment.GetEnvironmentVariable("ELMER_MCP_SERVERS");
        return string.IsNullOrWhiteSpace(env)
            ? ["RigReaper", "RigObs", "RigHeadrush", "RigCapCut"]
            : env.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public static async Task<IReadOnlyList<AIFunction>> CreateAsync(McpHub hub, Func<Action?>? actionStarted, CancellationToken ct = default)
    {
        async Task<List<AIFunction>> ForServer(string server)
        {
            var found = new List<AIFunction>();
            try
            {
                var client = hub.ByName(server);
                foreach (var tool in await client.ListToolsAsync(ct).ConfigureAwait(false))
                {
                    if (tool?["name"]?.GetValue<string>() is { Length: > 0 } name)
                    {
                        found.Add(new McpFunction(client, server, name, tool, actionStarted));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"MCP tools unavailable for {server}: {ex.Message}");
            }

            return found;
        }

        // Servers start independently, so list them in parallel.
        var perServer = await Task.WhenAll(ServerNames().Select(ForServer)).ConfigureAwait(false);
        return perServer.SelectMany(f => f).ToList();
    }

    public static bool IsRisky(string tool, JsonObject args) => tool switch
    {
        "render_project" or "load_rig" or "put_object_properties" or "call_object_method" => true,
        "call_reascript_function" => args["functionName"]?.GetValue<string>() is { } f && RiskyReaScript.IsMatch(f),
        "call_request" => args["requestType"]?.GetValue<string>() is { } r && RiskyObs.IsMatch(r),
        _ => false,
    };

    public static bool IsReadOnly(string tool, JsonObject args) =>
        ReadOnlyTool.IsMatch(tool)
        || (tool == "call_reascript_function" && args["functionName"]?.GetValue<string>() is { } f && ReadOnlyReaScript.IsMatch(f))
        || (tool == "call_request" && args["requestType"]?.GetValue<string>() is { } r && r.StartsWith("Get", StringComparison.Ordinal));

    private sealed class McpFunction : AIFunction
    {
        private readonly McpStdioClient _client;
        private readonly string _tool;
        private readonly Func<Action?>? _actionStarted;
        private readonly JsonElement _schema;

        public McpFunction(McpStdioClient client, string server, string tool, JsonNode definition, Func<Action?>? actionStarted)
        {
            _client = client;
            _tool = tool;
            _actionStarted = actionStarted;
            Name = $"mcp_{server.Replace("Rig", "").ToLowerInvariant()}_{tool}";
            Description = (definition["description"]?.GetValue<string>() ?? tool)
                + " Risky calls need confirmed=true after the Captain says yes.";

            var schema = definition["inputSchema"]?.DeepClone() as JsonObject
                ?? new JsonObject { ["type"] = "object" };
            if (schema["properties"] is not JsonObject props)
            {
                schema["properties"] = props = new JsonObject();
            }

            props["confirmed"] = new JsonObject
            {
                ["type"] = "boolean",
                ["description"] = "Set true only after the Captain clearly confirmed a risky action.",
            };
            _schema = JsonSerializer.SerializeToElement(schema);
        }

        public override string Name { get; }

        public override string Description { get; }

        public override JsonElement JsonSchema => _schema;

        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            var args = new JsonObject();
            var confirmed = false;
            foreach (var (key, value) in arguments)
            {
                var node = value is null ? null : JsonSerializer.SerializeToNode(value);
                if (key == "confirmed")
                {
                    confirmed = node?.GetValueKind() == JsonValueKind.True;
                    continue;
                }

                args[key] = node;
            }

            if (IsRisky(_tool, args) && !confirmed)
            {
                return NeedConfirm;
            }

            try
            {
                if (!IsReadOnly(_tool, args))
                {
                    _actionStarted?.Invoke()?.Invoke();
                }

                return await _client.CallToolAsync(_tool, args, TimeSpan.FromSeconds(60), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return "Failed: " + ex.Message;
            }
        }
    }
}
