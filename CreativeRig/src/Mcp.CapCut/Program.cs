using Mcp.CapCut;

var server = new CapCutMcpServer(new CapCutPythonBridge());

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

Console.Error.WriteLine("Mcp.CapCut starting (pyCapCut bridge over stdio)");
await server.RunStdioAsync(cts.Token).ConfigureAwait(false);
