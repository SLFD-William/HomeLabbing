using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Mcp.CapCut;

/// <summary>Runs scripts\capcut_bridge.py (pyCapCut) once per tool call.</summary>
public sealed class CapCutPythonBridge
{
    private readonly string _python = Environment.GetEnvironmentVariable("CAPCUT_BRIDGE_PYTHON") ?? "python";
    private readonly string _script = Path.Combine(AppContext.BaseDirectory, "scripts", "capcut_bridge.py");

    public async Task<JsonNode?> InvokeAsync(string command, string argumentsJson, CancellationToken ct)
    {
        if (!File.Exists(_script))
        {
            throw new FileNotFoundException($"Bridge script not found at '{_script}'.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _python,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(_script);
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
            throw new InvalidOperationException($"capcut_bridge.py produced no output. stderr: {stderr}");
        }

        var payload = JsonNode.Parse(stdout) ?? throw new InvalidOperationException("Invalid bridge JSON.");
        if (!(payload["success"]?.GetValue<bool>() ?? false))
        {
            throw new InvalidOperationException(payload["error"]?.GetValue<string>() ?? "Unknown bridge error.");
        }

        return payload["result"];
    }
}
