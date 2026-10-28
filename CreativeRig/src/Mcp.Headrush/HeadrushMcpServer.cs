using System.Text.Json;
using System.Text.Json.Nodes;
using Mcp.Core;

namespace Mcp.Headrush;

/// <summary>
/// MCP server exposing HeadRush Core/Prime rig control as tools:
/// load_rig, set_block_parameter, backup_rig.
///
/// Safety rules (from the community headrush-core-toolkit skill, carried
/// over deliberately): always back up via /subtree before an edit, never
/// overwrite a named rig unless explicitly asked, and confirm a rig load
/// actually completed (PresetName) before writing parameters into it.
/// </summary>
public sealed class HeadrushMcpServer : McpServerBase, IDisposable
{
    private readonly HeadrushClient _client;

    public HeadrushMcpServer(string baseUrl)
    {
        _client = new HeadrushClient(baseUrl);
    }

    protected override IEnumerable<McpToolDescriptor> GetTools() =>
    [
        new McpToolDescriptor
        {
            Name = "load_rig",
            Description = "Loads a named rig onto the HeadRush unit and waits for the load to complete.",
            InputSchema = """
                { "type": "object", "properties": {
                    "rigName": { "type": "string" },
                    "rigId": { "type": "integer" },
                    "setlistId": { "type": "integer", "default": -1 }
                  }, "required": ["rigName", "rigId"] }
                """,
        },
        new McpToolDescriptor
        {
            Name = "set_block_parameter",
            Description = "Sets a single parameter value on a block in the currently loaded rig.",
            InputSchema = """
                { "type": "object", "properties": {
                    "blockName": { "type": "string", "description": "e.g. G_EQ_2" },
                    "parameter": { "type": "string", "description": "e.g. Level" },
                    "value": {}
                  }, "required": ["blockName", "parameter", "value"] }
                """,
        },
        new McpToolDescriptor
        {
            Name = "backup_rig",
            Description = "Reads the full device subtree and writes it to a local JSON file for backup.",
            InputSchema = """
                { "type": "object", "properties": {
                    "backupPath": { "type": "string" }
                  }, "required": ["backupPath"] }
                """,
        },
        new McpToolDescriptor
        {
            Name = "get_subtree",
            Description = "Generic passthrough: reads the full object subtree at a path (or device root if omitted). Use to discover object paths and current state before editing.",
            InputSchema = """
                { "type": "object", "properties": {
                    "objectPath": { "type": "string", "default": "" }
                  } }
                """,
        },
        new McpToolDescriptor
        {
            Name = "get_object_meta",
            Description = "Generic passthrough: reads metadata (types, ranges, option lists, read-only flags) for an object path, e.g. /Evil/Engine/Patch/G_EQ_2.",
            InputSchema = """
                { "type": "object", "properties": {
                    "objectPath": { "type": "string" }
                  }, "required": ["objectPath"] }
                """,
        },
        new McpToolDescriptor
        {
            Name = "get_object_properties",
            Description = "Generic passthrough: reads current property values for an object path.",
            InputSchema = """
                { "type": "object", "properties": {
                    "objectPath": { "type": "string" }
                  }, "required": ["objectPath"] }
                """,
        },
        new McpToolDescriptor
        {
            Name = "put_object_properties",
            Description = "Generic passthrough: writes one or more property values on an object path, e.g. objectPath='/Evil/Engine/Patch/G_EQ_2', properties={'Level': 0.6}.",
            InputSchema = """
                { "type": "object", "properties": {
                    "objectPath": { "type": "string" },
                    "properties": { "type": "object" }
                  }, "required": ["objectPath", "properties"] }
                """,
        },
        new McpToolDescriptor
        {
            Name = "call_object_method",
            Description = "Generic passthrough: calls any RPC method on any object path, e.g. objectPath='/Evil/API/Rigs', methodName='loadRigConfirm', args=[rigId, setlistId]. Covers every method in the device's Evil/* API, not just the curated load_rig/backup_rig tools.",
            InputSchema = """
                { "type": "object", "properties": {
                    "objectPath": { "type": "string" },
                    "methodName": { "type": "string" },
                    "args": { "type": "array" }
                  }, "required": ["objectPath", "methodName"] }
                """,
        },
    ];

    protected override async Task<McpResponse> ExecuteAsync(McpRequest request, CancellationToken ct)
    {
        return request.Tool switch
        {
            "load_rig" => await LoadRigAsync(request, ct).ConfigureAwait(false),
            "set_block_parameter" => await SetBlockParameterAsync(request, ct).ConfigureAwait(false),
            "backup_rig" => await BackupRigAsync(request, ct).ConfigureAwait(false),
            "get_subtree" => await GetSubtreeAsync(request, ct).ConfigureAwait(false),
            "get_object_meta" => await GetObjectMetaAsync(request, ct).ConfigureAwait(false),
            "get_object_properties" => await GetObjectPropertiesAsync(request, ct).ConfigureAwait(false),
            "put_object_properties" => await PutObjectPropertiesAsync(request, ct).ConfigureAwait(false),
            "call_object_method" => await CallObjectMethodAsync(request, ct).ConfigureAwait(false),
            _ => McpResponse.Fail(request.Id, $"Unknown tool '{request.Tool}'."),
        };
    }

    private async Task<McpResponse> LoadRigAsync(McpRequest request, CancellationToken ct)
    {
        var args = ParseArguments(request);
        var rigName = RequireString(args, "rigName");
        var rigId = RequireInt(args, "rigId");
        var setlistId = args["setlistId"]?.GetValue<int>() ?? -1;

        // loadRigConfirm(rigId, setlistId) - see references/api.md.
        await _client.CallMethodAsync("/Evil/API/Rigs", "loadRigConfirm", [rigId, setlistId], ct)
            .ConfigureAwait(false);

        var completed = await _client.WaitForRigNameAsync(rigName, TimeSpan.FromSeconds(10), ct)
            .ConfigureAwait(false);

        if (!completed)
        {
            return McpResponse.Fail(request.Id, $"Timed out waiting for rig '{rigName}' to finish loading.");
        }

        return McpResponse.Ok(request.Id, JsonSerializer.Serialize(new { loaded = rigName }));
    }

    private async Task<McpResponse> SetBlockParameterAsync(McpRequest request, CancellationToken ct)
    {
        var args = ParseArguments(request);
        var blockName = RequireString(args, "blockName");
        var parameter = RequireString(args, "parameter");
        var value = args["value"] ?? throw new ArgumentException("Missing required argument 'value'.");

        await _client.PutObjectPropertiesAsync(
            $"/Evil/Engine/Patch/{blockName}",
            new Dictionary<string, JsonNode?> { [parameter] = value.DeepClone() },
            ct).ConfigureAwait(false);

        return McpResponse.Ok(request.Id, JsonSerializer.Serialize(new { blockName, parameter, value }));
    }

    private async Task<McpResponse> BackupRigAsync(McpRequest request, CancellationToken ct)
    {
        var args = ParseArguments(request);
        var backupPath = RequireString(args, "backupPath");

        var subtree = await _client.GetSubtreeAsync(ct: ct).ConfigureAwait(false);
        var directory = Path.GetDirectoryName(Path.GetFullPath(backupPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(
            backupPath,
            subtree?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null",
            ct).ConfigureAwait(false);

        return McpResponse.Ok(request.Id, JsonSerializer.Serialize(new { backupPath }));
    }

    private async Task<McpResponse> GetSubtreeAsync(McpRequest request, CancellationToken ct)
    {
        var args = ParseArguments(request);
        var objectPath = args["objectPath"]?.GetValue<string>() ?? "";

        var subtree = await _client.GetSubtreeAsync(objectPath, ct).ConfigureAwait(false);
        return McpResponse.Ok(request.Id, subtree?.ToJsonString() ?? "null");
    }

    private async Task<McpResponse> GetObjectMetaAsync(McpRequest request, CancellationToken ct)
    {
        var args = ParseArguments(request);
        var objectPath = RequireString(args, "objectPath");

        var meta = await _client.GetObjectMetaAsync(objectPath, ct).ConfigureAwait(false);
        return McpResponse.Ok(request.Id, meta?.ToJsonString() ?? "null");
    }

    private async Task<McpResponse> GetObjectPropertiesAsync(McpRequest request, CancellationToken ct)
    {
        var args = ParseArguments(request);
        var objectPath = RequireString(args, "objectPath");

        var properties = await _client.GetObjectPropertiesAsync(objectPath, ct).ConfigureAwait(false);
        return McpResponse.Ok(request.Id, properties?.ToJsonString() ?? "null");
    }

    private async Task<McpResponse> PutObjectPropertiesAsync(McpRequest request, CancellationToken ct)
    {
        var args = ParseArguments(request);
        var objectPath = RequireString(args, "objectPath");
        var properties = args["properties"] as JsonObject
            ?? throw new ArgumentException("Missing required object argument 'properties'.");

        await _client.PutObjectPropertiesAsync(objectPath, properties.DeepClone(), ct).ConfigureAwait(false);
        return McpResponse.Ok(request.Id, JsonSerializer.Serialize(new { objectPath, properties }));
    }

    private async Task<McpResponse> CallObjectMethodAsync(McpRequest request, CancellationToken ct)
    {
        var args = ParseArguments(request);
        var objectPath = RequireString(args, "objectPath");
        var methodName = RequireString(args, "methodName");
        var methodArgs = (args["args"] as JsonArray)?.Select(node => (object?)node).ToArray() ?? [];

        var result = await _client.CallMethodAsync(objectPath, methodName, methodArgs, ct).ConfigureAwait(false);
        return McpResponse.Ok(request.Id, result?.ToJsonString() ?? "null");
    }

    private static JsonObject ParseArguments(McpRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ArgumentsJson))
        {
            return [];
        }

        return JsonNode.Parse(request.ArgumentsJson) as JsonObject
            ?? throw new ArgumentException("Arguments must be a JSON object.");
    }

    private static string RequireString(JsonObject args, string name) =>
        args[name]?.GetValue<string>()
            ?? throw new ArgumentException($"Missing required argument '{name}'.");

    private static int RequireInt(JsonObject args, string name) =>
        args[name]?.GetValue<int>()
            ?? throw new ArgumentException($"Missing required argument '{name}'.");

    public void Dispose() => _client.Dispose();
}
