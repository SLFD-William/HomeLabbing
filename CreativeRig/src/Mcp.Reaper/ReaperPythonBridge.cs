using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mcp.Reaper;

/// <summary>
/// Invokes scripts\reapy_bridge.py as a subprocess for each tool call. Each
/// invocation is a fresh Python process using python-reapy's "distant API"
/// to talk to an already-running REAPER instance (Enable_Reapy.py must have
/// been run once inside REAPER to enable the bridge for this session).
/// </summary>
public sealed class ReaperPythonBridge
{
    private readonly string _pythonExecutable;
    private readonly string _bridgeScriptPath;

    public ReaperPythonBridge(string? pythonExecutable = null, string? bridgeScriptPath = null)
    {
        _pythonExecutable = pythonExecutable
            ?? Environment.GetEnvironmentVariable("REAPER_BRIDGE_PYTHON")
            ?? "python";

        _bridgeScriptPath = bridgeScriptPath
            ?? Path.Combine(AppContext.BaseDirectory, "scripts", "reapy_bridge.py");
    }

    public async Task<JsonNode?> InvokeAsync(string command, string argumentsJson, CancellationToken ct)
    {
        if (!File.Exists(_bridgeScriptPath))
        {
            throw new FileNotFoundException($"Bridge script not found at '{_bridgeScriptPath}'.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _pythonExecutable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(_bridgeScriptPath);
        startInfo.ArgumentList.Add(command);
        startInfo.ArgumentList.Add(argumentsJson);

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        var stdout = (await stdoutTask.ConfigureAwait(false)).Trim();
        var stderr = (await stderrTask.ConfigureAwait(false)).Trim();

        if (string.IsNullOrEmpty(stdout))
        {
            throw new InvalidOperationException(
                $"reapy_bridge.py produced no output for command '{command}'. " +
                (string.IsNullOrEmpty(stderr) ? "(no stderr)" : $"stderr: {stderr}"));
        }

        var payload = JsonNode.Parse(stdout)
            ?? throw new InvalidOperationException($"reapy_bridge.py returned invalid JSON: {stdout}");

        var success = payload["success"]?.GetValue<bool>() ?? false;
        if (!success)
        {
            var error = payload["error"]?.GetValue<string>() ?? "Unknown error from reapy_bridge.py.";
            throw new InvalidOperationException(error);
        }

        return payload["result"];
    }
}
