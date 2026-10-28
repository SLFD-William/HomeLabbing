using Mcp.Headrush;

// HEADRUSH_API overrides the default, matching the community toolkit's
// convention for networks where mDNS (headrushcore.local) doesn't resolve.
var baseUrl = Environment.GetEnvironmentVariable("HEADRUSH_API")
    ?? "http://192.168.21.147/api/v1";

Console.Error.WriteLine($"Mcp.Headrush starting against {baseUrl}");

using var server = new HeadrushMcpServer(baseUrl);
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

await server.RunStdioAsync(cts.Token).ConfigureAwait(false);
