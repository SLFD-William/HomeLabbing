namespace Voice.Commands;

public abstract record VoiceCommand;

public enum Target
{
    Reaper,
    Obs,
}

public enum TrackAction
{
    Mute,
    Unmute,
    Solo,
    Unsolo,
}

public enum TransportAction
{
    Play,
    Pause,
    GoToStart,
    Save,
    Undo,
    Redo,
    MetronomeOn,
    MetronomeOff,
    MetronomeToggle,
}

// Studio
public sealed record LaunchStudio : VoiceCommand;

public sealed record CloseStudio : VoiceCommand;

public enum StudioApp
{
    Reaper,
    Obs,
    Headrush,
    GForce,
    VsCode,
}

public sealed record LaunchApp(StudioApp App) : VoiceCommand;

public sealed record CloseApp(StudioApp App) : VoiceCommand;

// REAPER
public sealed record SetTempo(double Bpm) : VoiceCommand;

public sealed record GetTempo : VoiceCommand;

public sealed record StartRecording(Target Target = Target.Reaper) : VoiceCommand;

public sealed record StopRecording(Target Target = Target.Reaper) : VoiceCommand;

public sealed record Transport(TransportAction Action) : VoiceCommand;

public sealed record TrackState(TrackAction Action, int Track) : VoiceCommand;

public sealed record ApplyMood(string Mood, int Track) : VoiceCommand;

public sealed record RenderProject : VoiceCommand;

public sealed record AnalyzeAudio : VoiceCommand;

// OBS
public sealed record ObsScene(string Name) : VoiceCommand;

public sealed record ObsStream(bool Start) : VoiceCommand;

public sealed record ObsSource(string Name, bool Visible) : VoiceCommand;

public sealed record ObsMute(string Name, bool Muted) : VoiceCommand;

// HeadRush
public sealed record HeadrushBackup : VoiceCommand;

public sealed record HeadrushWhichRig : VoiceCommand;

public sealed record HeadrushLoadRig(string Name) : VoiceCommand;

// CapCut
public sealed record CapCutList : VoiceCommand;

public sealed record CapCutCreate(string Name) : VoiceCommand;

public sealed record CapCutSave(string Name) : VoiceCommand;

public sealed record CapCutAddText(string Draft, string Text) : VoiceCommand;
