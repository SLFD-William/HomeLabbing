using Mcp.Reaper;

var bridge = new ReaperPythonBridge();
var server = new ReaperMcpServer(bridge);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

Console.Error.WriteLine("Mcp.Reaper starting (reapy bridge over stdio)");
await server.RunStdioAsync(cts.Token).ConfigureAwait(false);
