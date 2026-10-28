using Voice.Commands;
using Xunit;

namespace Voice.Commands.Tests;

public class MultiServerParserTests
{
    private static VoiceCommand? Cmd(string text) => CommandParser.Parse("Hey Elmer, " + text).Command;

    public static TheoryData<string, VoiceCommand> Cases => new()
    {
        { "what's the tempo", new GetTempo() },
        { "start recording", new StartRecording() },
        { "stop recording in OBS", new StopRecording(Target.Obs) },
        { "start recording in obs", new StartRecording(Target.Obs) },
        { "stop", new StopRecording() },
        { "launch studio", new LaunchStudio() },
        { "launch reaper", new LaunchApp(StudioApp.Reaper) },
        { "open OBS", new LaunchApp(StudioApp.Obs) },
        { "start the Head Rush", new LaunchApp(StudioApp.Headrush) },
        { "launch G-Force", new LaunchApp(StudioApp.GForce) },
        { "close VS Code", new CloseApp(StudioApp.VsCode) },
        { "open Visual Studio Code", new LaunchApp(StudioApp.VsCode) },
        { "close reaper", new CloseApp(StudioApp.Reaper) },
        { "kill OBS", new CloseApp(StudioApp.Obs) },
        { "start recording in obs", new StartRecording(Target.Obs) },
        { "kill the studio", new CloseStudio() },
        { "shut down the studio", new CloseStudio() },
        { "play", new Transport(TransportAction.Play) },
        { "pause", new Transport(TransportAction.Pause) },
        { "go to the start", new Transport(TransportAction.GoToStart) },
        { "save the project", new Transport(TransportAction.Save) },
        { "undo", new Transport(TransportAction.Undo) },
        { "turn the metronome on", new Transport(TransportAction.MetronomeOn) },
        { "metronome off", new Transport(TransportAction.MetronomeOff) },
        { "toggle the metronome", new Transport(TransportAction.MetronomeToggle) },
        { "mute track 3", new TrackState(TrackAction.Mute, 3) },
        { "unmute track two", new TrackState(TrackAction.Unmute, 2) },
        { "solo track to", new TrackState(TrackAction.Solo, 2) },
        { "apply dreamy to track 2", new ApplyMood("dreamy", 2) },
        { "render the project", new RenderProject() },
        { "analyze the selected audio", new AnalyzeAudio() },
        { "switch to the Gaming scene in OBS", new ObsScene("Gaming") },
        { "go to scene Intro", new ObsScene("Intro") },
        { "start streaming", new ObsStream(true) },
        { "go live", new ObsStream(true) },
        { "stop the stream", new ObsStream(false) },
        { "hide the webcam in OBS", new ObsSource("webcam", false) },
        { "show Logo source", new ObsSource("Logo", true) },
        { "mute the mic in O.B.S.", new ObsMute("mic", true) },
        { "which rig is loaded on the HeadRush", new HeadrushWhichRig() },
        { "back up the Headrush rig", new HeadrushBackup() },
        { "load the rig GJordan", new HeadrushLoadRig("GJordan") },
        { "load the clean rig on the head rush", new HeadrushLoadRig("clean") },
        { "list my CapCut drafts", new CapCutList() },
        { "create a Cap Cut draft called Promo", new CapCutCreate("Promo") },
        { "save the CapCut draft Promo", new CapCutSave("Promo") },
        { "add text Hello World to CapCut draft Promo", new CapCutAddText("Promo", "Hello World") },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void ParsesCommand(string text, VoiceCommand expected) => Assert.Equal(expected, Cmd(text));

    [Fact]
    public void MoodWithoutTrackIsInvalid() =>
        Assert.Equal(ParseStatus.Invalid, CommandParser.Parse("Hey Elmer, make it dreamy").Status);

    [Fact]
    public void UnknownServerRequestIsUnrecognized() =>
        Assert.Equal(ParseStatus.Unrecognized, CommandParser.Parse("Hey Elmer, order a pizza").Status);

    [Fact]
    public void FuzzyMatchesSpokenNames()
    {
        Assert.Equal("Main Cam", Fuzzy.Best("main cam", ["Scene", "Main Cam"]));
        Assert.Null(Fuzzy.Best("zebra", ["Scene", "Main Cam"]));
    }
}
