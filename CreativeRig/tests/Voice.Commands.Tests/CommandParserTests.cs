using Voice.Commands;
using Xunit;

namespace Voice.Commands.Tests;

public class CommandParserTests
{
    [Theory]
    [InlineData("Hey Elmer, set the project tempo in Reaper to 171 beats per minute", 171)]
    [InlineData("Hey Elmer, set the project tempo in Reaper to 171 beats per minute.", 171)]
    [InlineData("hey elmer set tempo to 171 BPM", 171)]
    [InlineData("Elmer, tempo 90", 90)]
    [InlineData("Hey Elmer, change the tempo to one seventy one", 171)]
    [InlineData("Hey Elmer, set the tempo to one hundred and seventy one beats per minute", 171)]
    [InlineData("Hey Elmer, set the tempo to ninety", 90)]
    [InlineData("Hey Elmer, set the tempo to seventy two", 72)]
    [InlineData("Hey Elmer, set the tempo to 128.5 bpm", 128.5)]
    [InlineData("Hey Elmar, set the tempo to 120", 120)]
    public void ParsesTempo(string transcript, double bpm)
    {
        var r = CommandParser.Parse(transcript);
        Assert.Equal(ParseStatus.Command, r.Status);
        Assert.Equal(new SetTempo(bpm), r.Command);
    }

    [Theory]
    [InlineData("set the tempo to 171")]
    [InlineData("hey rig, set the tempo to 171")]
    public void IgnoresTranscriptsNotAddressedToElmer(string transcript) =>
        Assert.Equal(ParseStatus.NotAddressed, CommandParser.Parse(transcript).Status);

    [Theory]
    [InlineData("Hey Elmer, set the tempo to 5000")]
    [InlineData("Hey Elmer, set the tempo to 1")]
    [InlineData("Hey Elmer, set the tempo")]
    public void RejectsBadTempo(string transcript) =>
        Assert.Equal(ParseStatus.Invalid, CommandParser.Parse(transcript).Status);

    [Fact]
    public void WakeWordAfterChatterAndRepeated()
    {
        Assert.Equal(ParseStatus.WakeOnly, CommandParser.Parse("Jacobooda, pa pa, hey Elmer. Hey Elmer, hey Elmer.").Status);
        Assert.Equal(new SetTempo(171), CommandParser.Parse("blah blah hey Elmer, set the tempo to 171").Command);
    }

    [Fact]
    public void ParsesTransport()
    {
        Assert.IsType<StartRecording>(CommandParser.Parse("Hey Elmer, start recording").Command);
        Assert.IsType<StopRecording>(CommandParser.Parse("Hey Elmer, stop recording").Command);
        Assert.IsType<StopRecording>(CommandParser.Parse("Hey Elmer, stop").Command);
    }

    [Fact]
    public void UnknownRequestIsUnrecognized() =>
        Assert.Equal(ParseStatus.Unrecognized, CommandParser.Parse("Hey Elmer, make me a sandwich").Status);
}
