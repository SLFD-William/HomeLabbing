using System.Text.Json.Serialization;

namespace Mcp.Core;

/// <summary>
/// Describes a single tool exposed by an MCP server: its name, purpose, and
/// the JSON Schema for the arguments it accepts.
/// </summary>
public sealed class McpToolDescriptor
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>JSON Schema (as a raw JSON string) describing the tool's input arguments.</summary>
    [JsonPropertyName("inputSchema")]
    public required string InputSchema { get; init; }
}

/// <summary>
/// A single incoming tool invocation request, as read from the MCP transport.
/// </summary>
public sealed class McpRequest
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("tool")]
    public required string Tool { get; init; }

    /// <summary>Raw JSON of the tool arguments, deserialized by the tool handler itself.</summary>
    [JsonPropertyName("arguments")]
    public string? ArgumentsJson { get; init; }
}

/// <summary>
/// The result of executing an <see cref="McpRequest"/>, written back over the transport.
/// </summary>
public sealed class McpResponse
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("success")]
    public required bool Success { get; init; }

    /// <summary>Raw JSON of the tool result, when <see cref="Success"/> is true.</summary>
    [JsonPropertyName("result")]
    public string? ResultJson { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    public static McpResponse Ok(string id, string resultJson) =>
        new() { Id = id, Success = true, ResultJson = resultJson };

    public static McpResponse Fail(string id, string error) =>
        new() { Id = id, Success = false, Error = error };
}
