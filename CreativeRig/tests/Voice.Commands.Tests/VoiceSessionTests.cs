using Voice.Commands;
using Xunit;

namespace Voice.Commands.Tests;

public class VoiceSessionTests
{
    private static (VoiceSession Session, List<string> Said, Action<TimeSpan> Advance) Create()
    {
        var said = new List<string>();
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var session = new VoiceSession(new CommandExecutor(null), said.Add, () => now);
        return (session, said, d => now += d);
    }

    [Fact]
    public async Task WakeWordThenCommand()
    {
        var (s, said, _) = Create();

        Assert.Equal("LISTENING", await s.HandleAsync("Hey Elmer."));
        Assert.Equal(["Aye Captain"], said);

        Assert.Equal("HANDLED [dry-run] SetTempo { Bpm = 171 }", await s.HandleAsync("Set the tempo to 171 beats per minute."));
        Assert.Equal(["Aye Captain", "Aye Aye"], said);
        Assert.False(s.IsListening);
    }

    [Fact]
    public async Task WakeWordAndCommandInOneUtterance()
    {
        var (s, said, _) = Create();
        Assert.Equal("HANDLED [dry-run] SetTempo { Bpm = 171 }",
            await s.HandleAsync("Hey Elmer, set the project tempo in Reaper to 171 beats per minute"));
        Assert.Equal(["Aye Captain", "Aye Aye"], said);
    }

    [Fact]
    public async Task SpeechWithoutWakeWordIsIgnored()
    {
        var (s, said, _) = Create();
        Assert.Equal("PASS", await s.HandleAsync("set the tempo to 171"));
        Assert.Empty(said);
    }

    [Fact]
    public async Task ListeningWindowExpires()
    {
        var (s, said, advance) = Create();
        await s.HandleAsync("Hey Elmer");
        advance(VoiceSession.ListenWindow + TimeSpan.FromSeconds(1));

        Assert.Equal("PASS", await s.HandleAsync("set the tempo to 171"));
        Assert.Equal(["Aye Captain"], said);
    }

    [Fact]
    public async Task MisheardCommandKeepsListening()
    {
        var (s, said, _) = Create();
        await s.HandleAsync("Hey Elmer");

        Assert.Equal("LISTENING", await s.HandleAsync("make me a sandwich"));
        Assert.True(s.IsListening);

        Assert.StartsWith("HANDLED", await s.HandleAsync("start recording"));
        Assert.Equal("Aye Aye", said[^1]);
    }
}
