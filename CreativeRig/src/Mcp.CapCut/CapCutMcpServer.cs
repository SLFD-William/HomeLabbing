using Mcp.Core;

namespace Mcp.CapCut;

/// <summary>
/// CapCut tools backed by pyCapCut. Edits accumulate per draft name and are written
/// to CapCut's drafts folder by save_draft (CapCut must be closed).
/// </summary>
public sealed class CapCutMcpServer : McpServerBase
{
    private readonly CapCutPythonBridge _bridge;

    public CapCutMcpServer(CapCutPythonBridge bridge) => _bridge = bridge;

    private static McpToolDescriptor Tool(string name, string description, string schema) =>
        new() { Name = name, Description = description, InputSchema = schema };

    protected override IEnumerable<McpToolDescriptor> GetTools() =>
    [
        Tool("create_draft", "Starts a new draft spec (width/height/fps default 1920x1080@30).",
            """{ "type": "object", "properties": { "name": {"type":"string"}, "width": {"type":"integer"}, "height": {"type":"integer"}, "fps": {"type":"integer"} }, "required": ["name"] }"""),
        Tool("add_video", "Adds a video clip. Times in seconds; clips on the same track must not overlap.",
            """{ "type": "object", "properties": { "name": {"type":"string"}, "path": {"type":"string"}, "start": {"type":"number"}, "duration": {"type":"number"}, "track": {"type":"string"}, "volume": {"type":"number"} }, "required": ["name","path","start","duration"] }"""),
        Tool("add_audio", "Adds an audio clip. Times in seconds.",
            """{ "type": "object", "properties": { "name": {"type":"string"}, "path": {"type":"string"}, "start": {"type":"number"}, "duration": {"type":"number"}, "track": {"type":"string"}, "volume": {"type":"number"} }, "required": ["name","path","start","duration"] }"""),
        Tool("add_text", "Adds a text overlay. color is [r,g,b] in 0..1.",
            """{ "type": "object", "properties": { "name": {"type":"string"}, "text": {"type":"string"}, "start": {"type":"number"}, "duration": {"type":"number"}, "size": {"type":"number"}, "color": {"type":"array"}, "track": {"type":"string"} }, "required": ["name","text","start","duration"] }"""),
        Tool("save_draft", "Writes the draft into CapCut's drafts folder (backs up an existing draft; refuses while CapCut is running unless force=true).",
            """{ "type": "object", "properties": { "name": {"type":"string"}, "force": {"type":"boolean"} }, "required": ["name"] }"""),
        Tool("list_drafts", "Lists CapCut drafts and pending specs.", """{ "type": "object", "properties": {} }"""),
        Tool("read_draft", "Summarizes an existing draft's canvas, fps, duration and tracks.",
            """{ "type": "object", "properties": { "name": {"type":"string"} }, "required": ["name"] }"""),
        Tool("patch_draft_json", "Generic passthrough: deep-merges a JSON patch into an existing draft's draft_content.json (backup made; refuses while CapCut is running unless force=true).",
            """{ "type": "object", "properties": { "name": {"type":"string"}, "patch": {"type":"object"}, "force": {"type":"boolean"} }, "required": ["name","patch"] }"""),
    ];

    private static readonly HashSet<string> Known =
        ["create_draft", "add_video", "add_audio", "add_text", "save_draft", "list_drafts", "read_draft", "patch_draft_json"];

    protected override async Task<McpResponse> ExecuteAsync(McpRequest request, CancellationToken ct)
    {
        if (!Known.Contains(request.Tool))
        {
            return McpResponse.Fail(request.Id, $"Unknown tool '{request.Tool}'.");
        }

        try
        {
            var args = string.IsNullOrWhiteSpace(request.ArgumentsJson) ? "{}" : request.ArgumentsJson;
            var result = await _bridge.InvokeAsync(request.Tool, args, ct).ConfigureAwait(false);
            return McpResponse.Ok(request.Id, result?.ToJsonString() ?? "null");
        }
        catch (Exception ex)
        {
            return McpResponse.Fail(request.Id, ex.Message);
        }
    }
}
