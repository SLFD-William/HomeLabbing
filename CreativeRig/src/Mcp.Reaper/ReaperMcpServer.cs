using System.Text.Json;
using System.Text.Json.Nodes;
using Mcp.Core;

namespace Mcp.Reaper;

/// <summary>
/// MCP server exposing REAPER control as tools, per the design doc §6.3:
/// analyze_audio_region, apply_fx_chain_for_mood, render_project,
/// start_recording, stop_recording.
///
/// Each tool call shells out to scripts\reapy_bridge.py, which talks to a
/// running REAPER instance via python-reapy's distant API.
/// </summary>
public sealed class ReaperMcpServer : McpServerBase
{
    private readonly ReaperPythonBridge _bridge;

    public ReaperMcpServer(ReaperPythonBridge bridge)
    {
        _bridge = bridge;
    }

    protected override IEnumerable<McpToolDescriptor> GetTools() =>
    [
        new McpToolDescriptor
        {
            Name = "analyze_audio_region",
            Description = "Analyzes the currently selected media item's audio (duration, peak dB, RMS dB, integrated LUFS, estimated tempo).",
            InputSchema = """{ "type": "object", "properties": {} }""",
        },
        new McpToolDescriptor
        {
            Name = "apply_fx_chain_for_mood",
            Description = "Adds a stock-REAPER FX chain suited to a named mood (dreamy, aggressive, warm, spacious, clean) onto a track.",
            InputSchema = """
                { "type": "object", "properties": {
                    "trackIndex": { "type": "integer", "default": 0 },
                    "mood": { "type": "string" }
                  }, "required": ["mood"] }
                """,
        },
        new McpToolDescriptor
        {
            Name = "render_project",
            Description = "Renders the project using REAPER's most recently used render settings.",
            InputSchema = """{ "type": "object", "properties": {} }""",
        },
        new McpToolDescriptor
        {
            Name = "start_recording",
            Description = "Starts REAPER's transport recording.",
            InputSchema = """{ "type": "object", "properties": {} }""",
        },
        new McpToolDescriptor
        {
            Name = "stop_recording",
            Description = "Stops REAPER's transport.",
            InputSchema = """{ "type": "object", "properties": {} }""",
        },
        new McpToolDescriptor
        {
            Name = "call_reascript_function",
            Description = "Generic passthrough to any raw ReaScript API function (734+ available), e.g. functionName='GetTrack', args=[0,0]. Object handles (MediaTrack*, MediaItem*, etc.) are returned as opaque strings that can be passed into later calls unchanged.",
            InputSchema = """
                { "type": "object", "properties": {
                    "functionName": { "type": "string" },
                    "args": { "type": "array" }
                  }, "required": ["functionName"] }
                """,
        },
        new McpToolDescriptor
        {
            Name = "list_reascript_functions",
            Description = "Lists available raw ReaScript function names, optionally filtered by a substring (e.g. 'Track', 'FX', 'Midi').",
            InputSchema = """
                { "type": "object", "properties": {
                    "filter": { "type": "string" }
                  } }
                """,
        },
    ];

    protected override async Task<McpResponse> ExecuteAsync(McpRequest request, CancellationToken ct)
    {
        var knownTools = new HashSet<string>(
        [
            "analyze_audio_region",
            "apply_fx_chain_for_mood",
            "render_project",
            "start_recording",
            "stop_recording",
            "call_reascript_function",
            "list_reascript_functions",
        ]);

        if (!knownTools.Contains(request.Tool))
        {
            return McpResponse.Fail(request.Id, $"Unknown tool '{request.Tool}'.");
        }

        try
        {
            var argumentsJson = string.IsNullOrWhiteSpace(request.ArgumentsJson) ? "{}" : request.ArgumentsJson;
            var result = await _bridge.InvokeAsync(request.Tool, argumentsJson, ct).ConfigureAwait(false);
            return McpResponse.Ok(request.Id, result?.ToJsonString() ?? "null");
        }
        catch (Exception ex)
        {
            return McpResponse.Fail(request.Id, ex.Message);
        }
    }
}
