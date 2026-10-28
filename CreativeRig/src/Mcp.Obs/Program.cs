using System.Text.Json.Nodes;
using Mcp.Obs;

var url = Environment.GetEnvironmentVariable("OBS_WS_URL") ?? "ws://127.0.0.1:4455";
var password = Environment.GetEnvironmentVariable("OBS_WS_PASSWORD") ?? ReadPasswordFromObsConfig();

using var server = new ObsMcpServer(new ObsClient(url, password));
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

Console.Error.WriteLine($"Mcp.Obs starting against {url}");
await server.RunStdioAsync(cts.Token).ConfigureAwait(false);

// Falls back to the password stored by OBS so it never has to be hardcoded or committed.
static string? ReadPasswordFromObsConfig()
{
    var path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "obs-studio", "plugin_config", "obs-websocket", "config.json");
    return File.Exists(path)
        ? JsonNode.Parse(File.ReadAllText(path))?["server_password"]?.GetValue<string>()
        : null;
}
