using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using NAudio.Wave;

namespace Voice.Commands;

/// <summary>
/// Speaks replies one at a time on a chosen output device. VOICE_OUTPUT is a device name
/// substring (e.g. "Realtek"); unset uses the system default. VOICE_NAME picks the voice.
///
/// .NET's System.Speech cannot create a voice engine here, so a persistent Windows PowerShell
/// helper renders each phrase to a wav file (cached) and NAudio plays it.
/// </summary>
public sealed class SpeechOutput : IDisposable
{
    private const string HelperScript = """
        $ProgressPreference = 'SilentlyContinue'
        Add-Type -AssemblyName System.Speech
        $s = New-Object System.Speech.Synthesis.SpeechSynthesizer
        $want = $env:VOICE_NAME
        $names = $s.GetInstalledVoices() | Where-Object { $_.Enabled } | ForEach-Object { $_.VoiceInfo.Name }
        $pick = $names | Where-Object { $want -and $_ -like "*$want*" } | Select-Object -First 1
        if (-not $pick) { $pick = $names | Where-Object { $_ -like '*David*' } | Select-Object -First 1 }
        if ($pick) { $s.SelectVoice($pick) }
        [Console]::Out.WriteLine('READY')
        while ($true) {
            $line = [Console]::In.ReadLine()
            if ($null -eq $line) { break }
            $path, $text = $line -split "`t", 2
            try { $s.SetOutputToWaveFile($path); $s.Speak($text); $s.SetOutputToNull(); [Console]::Out.WriteLine('OK') }
            catch { [Console]::Out.WriteLine('ERR ' + $_.Exception.Message) }
        }
        """;

    private readonly BlockingCollection<string> _queue = new();
    private readonly Thread _worker;
    private readonly int _deviceNumber = -1;
    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), "VoiceCommandsTts");
    private Process? _helper;
    private int _pending;

    public SpeechOutput(string? deviceHint)
    {
        if (!string.IsNullOrWhiteSpace(deviceHint))
        {
            for (var i = 0; i < WaveOut.DeviceCount; i++)
            {
                if (WaveOut.GetCapabilities(i).ProductName.Contains(deviceHint, StringComparison.OrdinalIgnoreCase))
                {
                    _deviceNumber = i;
                    break;
                }
            }
        }

        Directory.CreateDirectory(_cacheDir);
        _worker = new Thread(Run) { IsBackground = true };
        _worker.Start();
    }

    public static IEnumerable<string> DeviceNames =>
        Enumerable.Range(0, WaveOut.DeviceCount).Select(i => WaveOut.GetCapabilities(i).ProductName);

    public void Say(string text)
    {
        Interlocked.Increment(ref _pending);
        _queue.Add(text);
    }

    public void WaitUntilDone()
    {
        while (Volatile.Read(ref _pending) > 0)
        {
            Thread.Sleep(30);
        }
    }

    private Process StartHelper()
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(HelperScript));
        var proc = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -EncodedCommand {encoded}")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("Could not start PowerShell speech helper.");

        if (proc.StandardOutput.ReadLine() != "READY")
        {
            throw new InvalidOperationException("Speech helper failed to start.");
        }

        return proc;
    }

    private string Render(string text)
    {
        var name = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(
            text + Environment.GetEnvironmentVariable("VOICE_NAME")))) + ".wav";
        var path = Path.Combine(_cacheDir, name);
        if (File.Exists(path))
        {
            return path;
        }

        if (_helper is not { HasExited: false })
        {
            _helper = StartHelper();
        }

        var tmp = path + ".tmp";
        _helper.StandardInput.WriteLine($"{tmp}\t{text}");
        _helper.StandardInput.Flush();
        var reply = _helper.StandardOutput.ReadLine();
        if (reply != "OK")
        {
            throw new InvalidOperationException(reply ?? "speech helper exited");
        }

        File.Move(tmp, path, overwrite: true);
        return path;
    }

    private void Run()
    {
        foreach (var phrase in new[] { "Aye Captain", "Aye Aye" })
        {
            try
            {
                Render(phrase);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"speech warm-up failed: {ex.Message}");
            }
        }

        foreach (var text in _queue.GetConsumingEnumerable())
        {
            try
            {
                using var reader = new AudioFileReader(Render(text));
                using var output = new WaveOutEvent { DeviceNumber = _deviceNumber };
                output.Init(reader);
                output.Play();
                while (output.PlaybackState == PlaybackState.Playing)
                {
                    Thread.Sleep(20);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"speech failed: {ex.Message}");
            }
            finally
            {
                Interlocked.Decrement(ref _pending);
            }
        }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        try
        {
            _helper?.StandardInput.Close();
            _helper?.WaitForExit(1000);
            if (_helper is { HasExited: false })
            {
                _helper.Kill(true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}
