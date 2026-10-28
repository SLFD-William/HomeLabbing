using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mcp.Headrush;

/// <summary>
/// Thin wrapper around the HeadRush Core/Prime local REST API
/// (http://{device}/api/v1), as documented in the community
/// headrush-core-toolkit reference and confirmed against a live device's
/// own web bundle.
///
/// Endpoints:
///   GET  /subtree{path}
///   GET  /object-meta{path}
///   GET  /object-properties{path}
///   PUT  /object-properties{path}
///   POST /object-method{path}/{method}   body: { "arguments": [...] }
/// </summary>
public sealed class HeadrushClient : IDisposable
{
    private readonly HttpClient _http;

    public HeadrushClient(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("Base URL must not be empty.", nameof(baseUrl));
        }

        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(10),
        };
    }

    /// <summary>
    /// Reads the full object subtree at the given path (or the device root
    /// if <paramref name="objectPath"/> is empty). Used for pre-edit backups.
    /// </summary>
    public async Task<JsonNode?> GetSubtreeAsync(string objectPath = "", CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"subtree{objectPath}", ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonNode.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
    }

    /// <summary>Reads metadata (types, ranges, options) for an object path.</summary>
    public async Task<JsonNode?> GetObjectMetaAsync(string objectPath, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"object-meta{objectPath}", ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonNode.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
    }

    /// <summary>Reads the current property values for an object path.</summary>
    public async Task<JsonNode?> GetObjectPropertiesAsync(string objectPath, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"object-properties{objectPath}", ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonNode.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
    }

    /// <summary>Writes one or more property values on an object path.</summary>
    public async Task PutObjectPropertiesAsync(string objectPath, object properties, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync($"object-properties{objectPath}", properties, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Calls an RPC method on an object path, e.g.
    /// CallMethodAsync("/Evil/Engine/Patch/Chain", "setModuleTypeInternal", 0, 42).
    /// </summary>
    public async Task<JsonNode?> CallMethodAsync(
        string objectPath, string methodName, object?[] arguments, CancellationToken ct = default)
    {
        var body = new { arguments };
        var response = await _http.PostAsJsonAsync($"object-method{objectPath}/{methodName}", body, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var payload = await JsonNode.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        return payload?["methodReturnValue"];
    }

    /// <summary>
    /// Polls <c>/Evil/Engine/Patch/Rig</c> for PresetName until it matches the
    /// expected rig name (or the timeout elapses), per the toolkit's safety
    /// rule to confirm a load/save actually finished before writing further.
    /// </summary>
    public async Task<bool> WaitForRigNameAsync(
        string expectedName, TimeSpan timeout, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var props = await GetObjectPropertiesAsync("/Evil/Engine/Patch/Rig", ct).ConfigureAwait(false);
            var currentName = props?["PresetName"]?.GetValue<string>();
            if (string.Equals(currentName, expectedName, StringComparison.Ordinal))
            {
                return true;
            }

            await Task.Delay(250, ct).ConfigureAwait(false);
        }

        return false;
    }

    public void Dispose() => _http.Dispose();
}
