using Microsoft.Extensions.AI;

namespace Voice.Commands;

/// <summary>A language model that answers spoken requests, optionally by calling tools.</summary>
public interface IConversationModel : IAsyncDisposable
{
    Task<string> RespondAsync(string userText, CancellationToken ct = default);
}

/// <summary>The Rig actions a language model may take; every call goes through the same executor as voice commands.</summary>
public static class ConversationTools
{
    public const string SystemPrompt = """
        You are Elmer, a hands-free studio assistant for a recording engineer. Address the user as "Captain".
        Your replies are spoken aloud: use one or two short plain sentences, no markdown, lists, or symbols.
        You control REAPER (recording, tempo, tracks, FX moods, render), OBS (scenes, sources, mute, streaming, recording)
        and a HeadRush guitar rig through the provided tools. Use a tool whenever the Captain asks for an action,
        then briefly say what happened. Track numbers start at 1.
        Render, loading a HeadRush rig, and starting or stopping a stream are disruptive: first ask the Captain to confirm,
        and only pass confirmed=true after they clearly say yes in this conversation.
        If a tool fails, say so plainly. If you can't do something with your tools, say that instead of guessing.
        """;

    private const string NeedConfirm = "Not done. Ask the Captain to confirm first, then call again with confirmed=true.";

    public static IReadOnlyList<AIFunction> Create(CommandExecutor exec)
    {
        async Task<string> Run(VoiceCommand c)
        {
            try
            {
                return (await exec.ExecuteAsync(c).ConfigureAwait(false)).Message;
            }
            catch (Exception ex)
            {
                return "Failed: " + ex.Message;
            }
        }

        Task<string> Gate(bool confirmed, VoiceCommand c) => confirmed ? Run(c) : Task.FromResult(NeedConfirm);

        return
        [
            AIFunctionFactory.Create((string app) => Enum.TryParse<StudioApp>(app, true, out var a)
                ? Run(new LaunchApp(a))
                : Task.FromResult("Failed: app must be Reaper, Obs, Headrush, GForce or VsCode."),
                "app_launch", "Launch one studio app: Reaper, Obs, Headrush (the rig page), GForce or VsCode."),
            AIFunctionFactory.Create((string app) => Enum.TryParse<StudioApp>(app, true, out var a)
                ? Run(new CloseApp(a))
                : Task.FromResult("Failed: app must be Reaper, Obs, Headrush, GForce or VsCode."),
                "app_close", "Close one app: Reaper or VsCode (they ask to save), Obs, Headrush or GForce."),
            AIFunctionFactory.Create(() => Run(new LaunchStudio()), "studio_launch", "Launch the whole studio: REAPER, OBS, rig page and the other apps."),
            AIFunctionFactory.Create((bool confirmed) => Gate(confirmed, new CloseStudio()), "studio_close", "Close the whole studio, force-closing REAPER and OBS. Unsaved work can be lost, so needs confirmed=true."),
            AIFunctionFactory.Create((double bpm) => bpm is < CommandParser.MinBpm or > CommandParser.MaxBpm
                ? Task.FromResult($"Failed: tempo must be {CommandParser.MinBpm}-{CommandParser.MaxBpm} BPM.")
                : Run(new SetTempo(bpm)), "reaper_set_tempo", "Set the REAPER project tempo in BPM."),
            AIFunctionFactory.Create(() => Run(new GetTempo()), "reaper_get_tempo", "Read the REAPER project tempo."),
            AIFunctionFactory.Create((string action) => Enum.TryParse<TransportAction>(action, true, out var a)
                ? Run(new Transport(a))
                : Task.FromResult("Failed: action must be one of " + string.Join(", ", Enum.GetNames<TransportAction>())),
                "reaper_transport", "REAPER transport and project: Play, Pause, GoToStart, Save, Undo, Redo, MetronomeOn, MetronomeOff, MetronomeToggle."),
            AIFunctionFactory.Create((bool start, string target) => Run(start
                ? new StartRecording(ParseTarget(target))
                : new StopRecording(ParseTarget(target))),
                "recording", "Start or stop recording. target is 'reaper' or 'obs'. Stopping 'reaper' stops the transport."),
            AIFunctionFactory.Create((string action, int track) => Enum.TryParse<TrackAction>(action, true, out var a)
                ? Run(new TrackState(a, track))
                : Task.FromResult("Failed: action must be Mute, Unmute, Solo or Unsolo."),
                "reaper_track", "Mute, Unmute, Solo or Unsolo a REAPER track (numbered from 1)."),
            AIFunctionFactory.Create((string mood, int track) => Run(new ApplyMood(mood.ToLowerInvariant(), track)),
                "reaper_apply_mood", "Add a stock FX chain to a track (numbered from 1). Mood is one of dreamy, aggressive, warm, spacious, clean."),
            AIFunctionFactory.Create(() => Run(new AnalyzeAudio()), "reaper_analyze_audio", "Analyze the selected media item: peak, loudness, tempo."),
            AIFunctionFactory.Create((bool confirmed) => Gate(confirmed, new RenderProject()), "reaper_render", "Render the project with the last-used render settings. Needs confirmed=true."),
            AIFunctionFactory.Create((string name) => Run(new ObsScene(name)), "obs_scene", "Switch the OBS program scene; the name is fuzzy-matched."),
            AIFunctionFactory.Create((string name, bool visible) => Run(new ObsSource(name, visible)), "obs_source", "Show or hide a source in the current OBS scene; fuzzy-matched."),
            AIFunctionFactory.Create((string name, bool muted) => Run(new ObsMute(name, muted)), "obs_mute", "Mute or unmute an OBS audio input; fuzzy-matched."),
            AIFunctionFactory.Create((bool start, bool confirmed) => Gate(confirmed, new ObsStream(start)), "obs_stream", "Start or stop the OBS live stream. Needs confirmed=true."),
            AIFunctionFactory.Create(() => Run(new HeadrushWhichRig()), "headrush_current_rig", "Name of the loaded HeadRush rig."),
            AIFunctionFactory.Create(() => Run(new HeadrushBackup()), "headrush_backup", "Back up the current HeadRush rig to a file."),
            AIFunctionFactory.Create((string name, bool confirmed) => Gate(confirmed, new HeadrushLoadRig(name)), "headrush_load_rig", "Load a HeadRush rig by name; fuzzy-matched. Needs confirmed=true."),
        ];
    }

    private static Target ParseTarget(string target) =>
        target.Contains("obs", StringComparison.OrdinalIgnoreCase) ? Target.Obs : Target.Reaper;
}
