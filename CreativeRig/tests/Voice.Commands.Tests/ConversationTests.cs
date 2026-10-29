using Voice.Commands;
using Xunit;

namespace Voice.Commands.Tests;

public class ConversationTests
{
    private sealed class FakeModel(string reply, bool acts = false) : IConversationModel
    {
        public List<string> Asked { get; } = [];

        public Action? ActionStarted { get; set; }

        public Task<string> RespondAsync(string userText, CancellationToken ct = default)
        {
            Asked.Add(userText);
            if (acts)
            {
                ActionStarted?.Invoke();
                ActionStarted?.Invoke();
            }

            return Task.FromResult(reply);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ModelActionsAreConfirmedWithAyeAyeOnceBeforeTheReply()
    {
        var said = new List<string>();
        var session = new VoiceSession(new CommandExecutor(null), said.Add, model: new FakeModel("Done.", acts: true));

        await session.HandleAsync("Hey Elmer, get me ready to track some guitar please");
        Assert.Equal(["Aye Captain", "Aye Aye", "Done."], said);
    }

    [Fact]
    public async Task UnrecognizedSpeechGoesToTheModelAndKeepsListening()
    {
        var said = new List<string>();
        var model = new FakeModel("Try sixty six.");
        var session = new VoiceSession(new CommandExecutor(null), said.Add, model: model);

        Assert.Equal("CHAT Try sixty six.", await session.HandleAsync("Hey Elmer, what's a good speed for a slow blues"));
        Assert.Equal(["what's a good speed for a slow blues"], model.Asked);
        Assert.Equal(["Aye Captain", "Try sixty six."], said);
        Assert.True(session.IsListening);
    }

    [Fact]
    public async Task RequestsGoToTheModelByDefault()
    {
        var model = new FakeModel("done");
        var session = new VoiceSession(new CommandExecutor(null), _ => { }, model: model);

        await session.HandleAsync("Hey Elmer, set the tempo to 100");
        Assert.Single(model.Asked);
    }

    [Theory]
    [InlineData("Hey Elmer, what's a good tempo for a slow blues")]
    [InlineData("Hey Elmer, I'd like you to render this whole project when you get a chance")]
    public void QuestionsAreNotMistakenForCommands(string text) =>
        Assert.Equal(ParseStatus.Unrecognized, CommandParser.Parse(text).Status);
}
