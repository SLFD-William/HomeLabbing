using System.Text.Json.Nodes;
using Mcp.Core;

namespace Mcp.Obs;

/// <summary>
/// MCP server for OBS Studio over obs-websocket v5. Curated tools: set_scene,
/// toggle_source, start_recording, stop_recording. call_request is a generic
/// passthrough to any OBS request type (GetSceneList, SetInputSettings, ...),
/// see https://github.com/obsproject/obs-websocket/blob/master/docs/generated/protocol.md
/// </summary>
public sealed class ObsMcpServer : McpServerBase, IDisposable
{
    private readonly ObsClient _client;

    public ObsMcpServer(ObsClient client)
    {
        _client = client;
    }

    protected override IEnumerable<McpToolDescriptor> GetTools() =>
    [
        new McpToolDescriptor
        {
            Name = "set_scene",
            Description = "Switches OBS's current program scene.",
            InputSchema = """
                { "type": "object", "properties": { "sceneName": { "type": "string" } },
                  "required": ["sceneName"] }
                """,
        },
        new McpToolDescriptor
        {
            Name = "toggle_source",
            Description = "Shows or hides a source (scene item) within a scene. If 'enabled' is omitted the current state is flipped.",
            InputSchema = """
                { "type": "object", "properties": {
                    "sceneName": { "type": "string" },
                    "sourceName": { "type": "string" },
                    "enabled": { "type": "boolean" }
                  }, "required": ["sceneName", "sourceName"] }
                """,
        },
        new McpToolDescriptor
        {
            Name = "start_recording",
            Description = "Starts OBS recording.",
            InputSchema = """{ "type": "object", "properties": {} }""",
        },
        new McpToolDescriptor
        {
            Name = "stop_recording",
            Description = "Stops OBS recording and returns the output file path.",
            InputSchema = """{ "type": "object", "properties": {} }""",
        },
        new McpToolDescriptor
        {
            Name = "call_request",
            Description = "Generic passthrough to any obs-websocket v5 request, e.g. requestType='GetSceneList', or 'SetInputSettings' with requestData. Covers the full OBS API (scenes, inputs, filters, streaming, transitions, outputs, config).",
            InputSchema = """
                { "type": "object", "properties": {
                    "requestType": { "type": "string" },
                    "requestData": { "type": "object" }
                  }, "required": ["requestType"] }
                """,
        },
    ];

    protected override async Task<McpResponse> ExecuteAsync(McpRequest request, CancellationToken ct)
    {
        try
        {
            var args = string.IsNullOrWhiteSpace(request.ArgumentsJson)
                ? new JsonObject()
                : JsonNode.Parse(request.ArgumentsJson) as JsonObject
                    ?? throw new ArgumentException("Arguments must be a JSON object.");

            JsonNode? result = request.Tool switch
            {
                "set_scene" => await _client.CallAsync(
                    "SetCurrentProgramScene",
                    new JsonObject { ["sceneName"] = Require(args, "sceneName") }, ct).ConfigureAwait(false),
                "toggle_source" => await ToggleSourceAsync(args, ct).ConfigureAwait(false),
                "start_recording" => await _client.CallAsync("StartRecord", null, ct).ConfigureAwait(false),
                "stop_recording" => await _client.CallAsync("StopRecord", null, ct).ConfigureAwait(false),
                "call_request" => await _client.CallAsync(
                    Require(args, "requestType"), args["requestData"], ct).ConfigureAwait(false),
                _ => throw new ArgumentException($"Unknown tool '{request.Tool}'."),
            };

            return McpResponse.Ok(request.Id, result?.ToJsonString() ?? "{}");
        }
        catch (Exception ex)
        {
            return McpResponse.Fail(request.Id, ex.Message);
        }
    }

    private async Task<JsonNode?> ToggleSourceAsync(JsonObject args, CancellationToken ct)
    {
        var sceneName = Require(args, "sceneName");
        var sourceName = Require(args, "sourceName");

        var idResponse = await _client.CallAsync(
            "GetSceneItemId",
            new JsonObject { ["sceneName"] = sceneName, ["sourceName"] = sourceName }, ct).ConfigureAwait(false);
        var itemId = idResponse?["sceneItemId"]?.GetValue<int>()
            ?? throw new InvalidOperationException($"Source '{sourceName}' not found in scene '{sceneName}'.");

        bool enabled;
        if (args["enabled"] is JsonValue explicitValue)
        {
            enabled = explicitValue.GetValue<bool>();
        }
        else
        {
            var state = await _client.CallAsync(
                "GetSceneItemEnabled",
                new JsonObject { ["sceneName"] = sceneName, ["sceneItemId"] = itemId }, ct).ConfigureAwait(false);
            enabled = !(state?["sceneItemEnabled"]?.GetValue<bool>() ?? false);
        }

        await _client.CallAsync(
            "SetSceneItemEnabled",
            new JsonObject { ["sceneName"] = sceneName, ["sceneItemId"] = itemId, ["sceneItemEnabled"] = enabled },
            ct).ConfigureAwait(false);

        return new JsonObject { ["sceneName"] = sceneName, ["sourceName"] = sourceName, ["enabled"] = enabled };
    }

    private static string Require(JsonObject args, string name) =>
        args[name]?.GetValue<string>()
            ?? throw new ArgumentException($"Missing required argument '{name}'.");

    public void Dispose() => _client.Dispose();
}
