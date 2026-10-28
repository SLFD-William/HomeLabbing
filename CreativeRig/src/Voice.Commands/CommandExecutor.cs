using System.Globalization;
using System.Text.Json.Nodes;

namespace Voice.Commands;

/// <summary>Result of running a command: a log line plus an optional sentence to speak after "Aye Aye".</summary>
public sealed record Outcome(string Message, string? Spoken = null);

/// <summary>Carries parsed voice commands out against the Rig MCP servers.</summary>
public sealed class CommandExecutor
{
    private const string RigsPath = "/Evil/API/Rigs";
    private const string CurrentRigPath = "/Evil/Engine/Patch/Rig";

    private readonly McpHub? _hub;

    /// <param name="hub">Null for dry-run: commands are described but not sent.</param>
    public CommandExecutor(McpHub? hub)
    {
        _hub = hub;
    }

    public async Task<Outcome> ExecuteAsync(VoiceCommand command, CancellationToken ct = default)
    {
        if (_hub is null)
        {
            return new Outcome("[dry-run] " + command);
        }

        return command switch
        {
            SetTempo t => await SetTempoAsync(t, ct),
            LaunchStudio => RunStudioScript("Captain Cilipourri Naught Studio.bat", "Launching the studio"),
            CloseStudio => RunStudioScript("Close Studio.bat", "Closing the studio"),
            LaunchApp a => LaunchStudioApp(a.App),
            CloseApp a => CloseStudioApp(a.App),
            GetTempo => await GetTempoAsync(ct),
            StartRecording { Target: Target.Obs } => await Text(_hub.Obs.CallToolAsync("start_recording", ct: ct), "OBS recording started"),
            StopRecording { Target: Target.Obs } => await Text(_hub.Obs.CallToolAsync("stop_recording", ct: ct), "OBS recording stopped"),
            StartRecording => await Text(_hub.Reaper.CallToolAsync("start_recording", ct: ct), "Recording started"),
            StopRecording => await Text(_hub.Reaper.CallToolAsync("stop_recording", ct: ct), "Stopped"),
            Transport t => await TransportAsync(t.Action, ct),
            TrackState t => await TrackStateAsync(t, ct),
            ApplyMood m => await Text(
                _hub.Reaper.CallToolAsync("apply_fx_chain_for_mood", new JsonObject { ["trackIndex"] = m.Track - 1, ["mood"] = m.Mood }, ct: ct),
                $"Applied {m.Mood} to track {m.Track}"),
            RenderProject => await Text(_hub.Reaper.CallToolAsync("render_project", timeout: TimeSpan.FromMinutes(10), ct: ct), "Render started"),
            AnalyzeAudio => await AnalyzeAsync(ct),
            ObsScene s => await ObsSceneAsync(s.Name, ct),
            ObsStream s => await ObsRequestAsync(s.Start ? "StartStream" : "StopStream", s.Start ? "Stream started" : "Stream stopped", ct),
            ObsSource s => await ObsSourceAsync(s, ct),
            ObsMute m => await ObsMuteAsync(m, ct),
            HeadrushBackup => await HeadrushBackupAsync(ct),
            HeadrushWhichRig => await HeadrushWhichAsync(ct),
            HeadrushLoadRig l => await HeadrushLoadAsync(l.Name, ct),
            CapCutList => await CapCutListAsync(ct),
            CapCutCreate c => await Text(_hub.CapCut.CallToolAsync("create_draft", new JsonObject { ["name"] = c.Name }, ct: ct), $"Created CapCut draft {c.Name}"),
            CapCutSave c => await Text(_hub.CapCut.CallToolAsync("save_draft", new JsonObject { ["name"] = c.Name }, ct: ct), $"Saved CapCut draft {c.Name}"),
            CapCutAddText a => await CapCutAddTextAsync(a, ct),
            _ => throw new NotSupportedException(command.GetType().Name),
        };
    }

    private static async Task<Outcome> Text(Task<string> call, string spoken)
    {
        var text = await call.ConfigureAwait(false);
        return new Outcome(string.IsNullOrWhiteSpace(text) ? spoken : $"{spoken}: {text}");
    }

    // ---- Studio ----------------------------------------------------------------------------

    private static string StudioDir()
    {
        var dir = Environment.GetEnvironmentVariable("ELMER_STUDIO_DIR");
        return string.IsNullOrWhiteSpace(dir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "Repository", "StudioLaunchAutomation")
            : dir;
    }

    private static readonly Dictionary<StudioApp, (string Name, string Process)> AppInfo = new()
    {
        [StudioApp.Reaper] = ("REAPER", "reaper"),
        [StudioApp.Obs] = ("OBS", "obs64"),
        [StudioApp.Headrush] = ("the HeadRush page", ""),
        [StudioApp.GForce] = ("G-Force", "G-Force Standalone.x64"),
        [StudioApp.VsCode] = ("VS Code", "Code"),
    };

    private static bool RigPageOpen()
    {
        using var ps = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe",
            "-NoProfile -Command \"if (Get-CimInstance Win32_Process -Filter \\\"Name='msedge.exe'\\\" | Where-Object { $_.CommandLine -like '*StudioRigApp*' }) { 'yes' }\"")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        var output = ps?.StandardOutput.ReadToEnd() ?? "";
        ps?.WaitForExit();
        return output.Contains("yes");
    }

    private static Outcome LaunchStudioApp(StudioApp app)
    {
        var (name, process) = AppInfo[app];
        var running = app == StudioApp.Headrush
            ? RigPageOpen()
            : process.Length > 0 && System.Diagnostics.Process.GetProcessesByName(process).Length > 0;
        if (running)
        {
            return new Outcome($"{name} is already running", $"{name} is already open.");
        }

        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var start = app switch
        {
            StudioApp.Reaper => new System.Diagnostics.ProcessStartInfo(
                @"C:\Program Files\REAPER (x64)\reaper.exe",
                $"\"{Path.Combine(user, @"AppData\Roaming\REAPER\ProjectTemplates\August 14 2026 Base Template.rpp")}\""),
            StudioApp.Obs => new System.Diagnostics.ProcessStartInfo(
                @"C:\Program Files\obs-studio\bin\64bit\obs64.exe", "--scene \"Captain Cilipourri Naught\"")
            {
                WorkingDirectory = @"C:\Program Files\obs-studio\bin\64bit",
            },
            StudioApp.Headrush => new System.Diagnostics.ProcessStartInfo(
                @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                $"--user-data-dir=\"{Path.Combine(user, @"AppData\Local\StudioRigApp")}\" --app=\"http://192.168.21.147/#rig\" --no-first-run"),
            StudioApp.VsCode => new System.Diagnostics.ProcessStartInfo(
                Path.Combine(user, @"AppData\Local\Programs\Microsoft VS Code\Code.exe"),
                $"--agents \"{Path.Combine(user, @"Documents\Repository")}\""),
            _ => new System.Diagnostics.ProcessStartInfo(@"C:\Program Files (x86)\SoundSpectrum\G-Force\G-Force Standalone.x64.exe"),
        };

        if (!File.Exists(start.FileName))
        {
            throw new CommandException($"I can't find {name}.");
        }

        start.UseShellExecute = true; // don't let the app inherit our stdout (the protocol channel)
        System.Diagnostics.Process.Start(start)?.Dispose();
        return new Outcome($"Launching {name}", $"Launching {name}.");
    }

    private static Outcome CloseStudioApp(StudioApp app)
    {
        var (name, _) = AppInfo[app];
        switch (app)
        {
            case StudioApp.Reaper:
                // Ask politely so REAPER can offer to save unsaved changes.
                foreach (var p in System.Diagnostics.Process.GetProcessesByName("reaper"))
                {
                    p.CloseMainWindow();
                    p.Dispose();
                }

                return new Outcome("Asked REAPER to close", "Closing REAPER. It may ask you to save.");
            case StudioApp.Obs:
                var script = Path.Combine(StudioDir(), "Close-OBS.ps1");
                if (!File.Exists(script))
                {
                    throw new CommandException("I can't find the OBS close script.");
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe",
                    $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"") { UseShellExecute = false, CreateNoWindow = true })?.Dispose();
                return new Outcome("Closing OBS", "Closing OBS.");
            case StudioApp.Headrush:
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe",
                    "-NoProfile -Command \"Get-CimInstance Win32_Process -Filter \\\"Name='msedge.exe'\\\" | Where-Object { $_.CommandLine -like '*StudioRigApp*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }\"")
                { UseShellExecute = false, CreateNoWindow = true })?.Dispose();
                return new Outcome("Closing the HeadRush page", "Closing the HeadRush page.");
            case StudioApp.VsCode:
                // Close each window politely so VS Code can prompt about unsaved files.
                foreach (var p in System.Diagnostics.Process.GetProcessesByName("Code"))
                {
                    if (p.MainWindowHandle != IntPtr.Zero)
                    {
                        p.CloseMainWindow();
                    }

                    p.Dispose();
                }

                return new Outcome("Asked VS Code to close", "Closing VS Code. It may ask you to save.");
            default:
                foreach (var proc in new[] { "G-Force Standalone.x64", "G-Force Toolbar" })
                {
                    foreach (var p in System.Diagnostics.Process.GetProcessesByName(proc))
                    {
                        p.Kill();
                        p.Dispose();
                    }
                }

                return new Outcome($"Closed {name}", $"Closed {name}.");
        }
    }

    private static Outcome RunStudioScript(string file, string message)
    {
        var dir = StudioDir();
        var path = Path.Combine(dir, file);
        if (!File.Exists(path))
        {
            throw new CommandException($"I can't find {file}.");
        }

        // Fire and forget: the scripts take a while and Elmer should stay responsive.
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c \"\"{path}\"\"")
        {
            WorkingDirectory = dir,
            UseShellExecute = false,
            CreateNoWindow = true,
        })?.Dispose();
        return new Outcome(message, message + ".");
    }

    // ---- REAPER ----------------------------------------------------------------------------

    private static string Clean(string s) => s.Trim().Trim('"');

    private async Task<string> ReaScript(string function, JsonArray args, CancellationToken ct) =>
        Clean(await _hub!.Reaper.CallReaScriptAsync(function, args, ct).ConfigureAwait(false));

    private async Task<Outcome> SetTempoAsync(SetTempo t, CancellationToken ct)
    {
        // proj 0 = the active project.
        await ReaScript("SetCurrentBPM", [0, t.Bpm, true], ct);
        var readBack = await ReaScript("Master_GetTempo", [], ct);
        if (!double.TryParse(readBack, CultureInfo.InvariantCulture, out var actual) || Math.Abs(actual - t.Bpm) > 0.01)
        {
            throw new InvalidOperationException($"REAPER reports tempo {readBack}, expected {t.Bpm:0.##}.");
        }

        return new Outcome($"Tempo set to {t.Bpm:0.##} BPM");
    }

    private async Task<Outcome> GetTempoAsync(CancellationToken ct)
    {
        var raw = await ReaScript("Master_GetTempo", [], ct);
        var bpm = double.TryParse(raw, CultureInfo.InvariantCulture, out var v) ? v.ToString("0.##", CultureInfo.InvariantCulture) : raw;
        return new Outcome($"Tempo is {bpm} BPM", $"The tempo is {bpm} beats per minute.");
    }

    private async Task<Outcome> TransportAsync(TransportAction action, CancellationToken ct)
    {
        if (action is TransportAction.MetronomeOn or TransportAction.MetronomeOff or TransportAction.MetronomeToggle)
        {
            const int metronome = 41745;
            var on = await ReaScript("GetToggleCommandState", [metronome], ct) == "1";
            var want = action switch
            {
                TransportAction.MetronomeOn => true,
                TransportAction.MetronomeOff => false,
                _ => !on,
            };
            if (want != on)
            {
                await ReaScript("Main_OnCommand", [metronome, 0], ct);
            }

            return new Outcome($"Metronome {(want ? "on" : "off")}");
        }

        var (id, label) = action switch
        {
            TransportAction.Play => (1007, "Playing"),
            TransportAction.Pause => (1008, "Paused"),
            TransportAction.GoToStart => (40042, "Moved to start"),
            TransportAction.Save => (40026, "Project saved"),
            TransportAction.Undo => (40029, "Undone"),
            TransportAction.Redo => (40030, "Redone"),
            _ => throw new NotSupportedException(action.ToString()),
        };
        await ReaScript("Main_OnCommand", [id, 0], ct);
        return new Outcome(label);
    }

    private async Task<Outcome> TrackStateAsync(TrackState t, CancellationToken ct)
    {
        var handle = await ReaScript("GetTrack", [0, t.Track - 1], ct);
        if (handle.Length == 0 || handle == "null" || handle.EndsWith("0x0", StringComparison.OrdinalIgnoreCase) || handle.EndsWith("00000000", StringComparison.Ordinal))
        {
            throw new CommandException($"There is no track {t.Track}.");
        }

        var (key, value, word) = t.Action switch
        {
            TrackAction.Mute => ("B_MUTE", 1.0, "muted"),
            TrackAction.Unmute => ("B_MUTE", 0.0, "unmuted"),
            TrackAction.Solo => ("I_SOLO", 2.0, "soloed"),
            _ => ("I_SOLO", 0.0, "unsoloed"),
        };
        await ReaScript("SetMediaTrackInfo_Value", [handle, key, value], ct);

        var readBack = await ReaScript("GetMediaTrackInfo_Value", [handle, key], ct);
        if (!double.TryParse(readBack, CultureInfo.InvariantCulture, out var actual) || (value == 0) != (actual == 0))
        {
            throw new InvalidOperationException($"REAPER reports {key}={readBack}, expected {value}.");
        }

        return new Outcome($"Track {t.Track} {word}", $"Track {t.Track} {word}.");
    }

    private async Task<Outcome> AnalyzeAsync(CancellationToken ct)
    {
        var text = await _hub!.Reaper.CallToolAsync("analyze_audio_region", ct: ct).ConfigureAwait(false);
        return new Outcome("Analysis: " + text, "Analysis done. " + Summarize(text));
    }

    private static string Summarize(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is JsonObject o)
            {
                var parts = new List<string>();
                foreach (var (key, label) in new[] { ("peakDb", "peak"), ("integratedLufs", "loudness"), ("estimatedTempo", "tempo") })
                {
                    if (o.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)).Value is JsonValue v
                        && v.TryGetValue<double>(out var d))
                    {
                        parts.Add($"{label} {d:0.#}");
                    }
                }

                return string.Join(", ", parts);
            }
        }
        catch (System.Text.Json.JsonException)
        {
        }

        return "";
    }

    // ---- OBS -------------------------------------------------------------------------------

    private async Task<JsonNode?> ObsJson(string request, JsonObject? data, CancellationToken ct)
    {
        var args = new JsonObject { ["requestType"] = request };
        if (data is not null)
        {
            args["requestData"] = data;
        }

        return await _hub!.Obs.CallJsonAsync("call_request", args, ct: ct).ConfigureAwait(false);
    }

    private async Task<Outcome> ObsRequestAsync(string request, string spoken, CancellationToken ct)
    {
        await ObsJson(request, null, ct);
        return new Outcome(spoken, spoken);
    }

    private static IEnumerable<string> Names(JsonNode? root, string array, string field) =>
        (FindProp(root, array) as JsonArray ?? []).Select(n => AsString(n?[field])).OfType<string>();

    private async Task<string> CurrentSceneAsync(CancellationToken ct) =>
        AsString(FindProp(await ObsJson("GetSceneList", null, ct), "currentProgramSceneName"))
        ?? throw new CommandException("I couldn't read the current OBS scene.");

    private async Task<Outcome> ObsSceneAsync(string spoken, CancellationToken ct)
    {
        var scenes = Names(await ObsJson("GetSceneList", null, ct), "scenes", "sceneName").ToList();
        var scene = Fuzzy.Best(spoken, scenes) ?? throw new CommandException($"I couldn't find an OBS scene called {spoken}.");
        await _hub!.Obs.CallToolAsync("set_scene", new JsonObject { ["sceneName"] = scene }, ct: ct).ConfigureAwait(false);
        return new Outcome($"OBS scene {scene}", $"Scene {scene}.");
    }

    private async Task<Outcome> ObsSourceAsync(ObsSource s, CancellationToken ct)
    {
        var scene = await CurrentSceneAsync(ct);
        var items = Names(await ObsJson("GetSceneItemList", new JsonObject { ["sceneName"] = scene }, ct), "sceneItems", "sourceName").ToList();
        var source = Fuzzy.Best(s.Name, items) ?? throw new CommandException($"I couldn't find a source called {s.Name} in {scene}.");
        await _hub!.Obs.CallToolAsync("toggle_source", new JsonObject { ["sceneName"] = scene, ["sourceName"] = source, ["enabled"] = s.Visible }, ct: ct).ConfigureAwait(false);
        var word = s.Visible ? "shown" : "hidden";
        return new Outcome($"{source} {word}", $"{source} {word}.");
    }

    private async Task<Outcome> ObsMuteAsync(ObsMute m, CancellationToken ct)
    {
        var inputs = Names(await ObsJson("GetInputList", null, ct), "inputs", "inputName").ToList();
        var input = Fuzzy.Best(m.Name, inputs) ?? throw new CommandException($"I couldn't find an OBS input called {m.Name}.");
        await ObsJson("SetInputMute", new JsonObject { ["inputName"] = input, ["inputMuted"] = m.Muted }, ct);
        var word = m.Muted ? "muted" : "unmuted";
        return new Outcome($"{input} {word}", $"{input} {word}.");
    }

    // ---- HeadRush --------------------------------------------------------------------------

    private static JsonNode? FindProp(JsonNode? node, string name)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var (k, v) in o)
                {
                    if (string.Equals(k, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return v;
                    }
                }

                foreach (var (_, v) in o)
                {
                    if (FindProp(v, name) is { } found)
                    {
                        return found;
                    }
                }

                break;
            case JsonArray a:
                foreach (var v in a)
                {
                    if (FindProp(v, name) is { } found)
                    {
                        return found;
                    }
                }

                break;
        }

        return null;
    }

    private static string? AsString(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : n?.ToString();

    private async Task<JsonNode?> HeadrushProps(string path, CancellationToken ct) =>
        await _hub!.Headrush.CallJsonAsync("get_object_properties", new JsonObject { ["objectPath"] = path }, ct: ct).ConfigureAwait(false);

    private async Task<string> CurrentRigAsync(CancellationToken ct) =>
        AsString(FindProp(await HeadrushProps(CurrentRigPath, ct), "PresetName"))
        ?? throw new CommandException("I couldn't read the current HeadRush rig.");

    private async Task<Outcome> HeadrushWhichAsync(CancellationToken ct)
    {
        var rig = await CurrentRigAsync(ct);
        return new Outcome($"HeadRush rig: {rig}", $"The current rig is {rig}.");
    }

    private async Task<Outcome> HeadrushBackupAsync(CancellationToken ct)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CreativeRig", "HeadrushBackups");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"headrush-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await _hub!.Headrush.CallToolAsync("backup_rig", new JsonObject { ["backupPath"] = file }, ct: ct).ConfigureAwait(false);
        return new Outcome($"Backed up HeadRush rig to {file}", "Rig backed up.");
    }

    private async Task<Outcome> HeadrushLoadAsync(string spoken, CancellationToken ct)
    {
        var props = await HeadrushProps(RigsPath, ct);
        var names = (FindProp(props, "AllRigNames") as JsonArray ?? []).Select(AsString).ToList();
        var ids = (FindProp(props, "AllRigIds") as JsonArray ?? []).Select(AsString).ToList();
        if (names.Count == 0 || names.Count != ids.Count)
        {
            throw new CommandException("I couldn't read the HeadRush rig list.");
        }

        if (string.Equals(AsString(FindProp(props, "dirty")), "true", StringComparison.OrdinalIgnoreCase))
        {
            throw new CommandException("The current rig has unsaved changes, so I won't load another one.");
        }

        var rig = Fuzzy.Best(spoken, names.OfType<string>()) ?? throw new CommandException($"I couldn't find a rig called {spoken}.");
        var id = ids[names.IndexOf(rig)];

        // loadRigConfirm takes (rigId, setlistId) as strings; an empty setlist loads from the rig list.
        await _hub!.Headrush.CallToolAsync("call_object_method", new JsonObject
        {
            ["objectPath"] = RigsPath,
            ["methodName"] = "loadRigConfirm",
            ["args"] = new JsonArray(id, ""),
        }, ct: ct).ConfigureAwait(false);

        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(500, ct).ConfigureAwait(false);
            if (string.Equals(await CurrentRigAsync(ct), rig, StringComparison.OrdinalIgnoreCase))
            {
                return new Outcome($"Loaded rig {rig}", $"Loaded {rig}.");
            }
        }

        throw new CommandException($"I asked for {rig} but HeadRush didn't switch to it.");
    }

    // ---- CapCut ----------------------------------------------------------------------------

    private async Task<Outcome> CapCutListAsync(CancellationToken ct)
    {
        var json = await _hub!.CapCut.CallJsonAsync("list_drafts", ct: ct).ConfigureAwait(false);
        var drafts = (json as JsonArray ?? FindProp(json, "drafts") as JsonArray ?? [])
            .Select(d => AsString(d is JsonObject o ? FindProp(o, "name") : d))
            .OfType<string>()
            .Where(n => !n.StartsWith('.'))
            .ToList();
        return new Outcome("CapCut drafts: " + string.Join(", ", drafts),
            drafts.Count == 0 ? "There are no CapCut drafts." : $"You have {drafts.Count} CapCut drafts: {string.Join(", ", drafts)}.");
    }

    private async Task<Outcome> CapCutAddTextAsync(CapCutAddText a, CancellationToken ct)
    {
        await _hub!.CapCut.CallToolAsync("add_text", new JsonObject
        {
            ["name"] = a.Draft,
            ["text"] = a.Text,
            ["start"] = 0,
            ["duration"] = 3,
        }, ct: ct).ConfigureAwait(false);
        return new Outcome($"Added text '{a.Text}' to {a.Draft}", "Text added.");
    }
}
