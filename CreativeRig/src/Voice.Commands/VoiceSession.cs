namespace Voice.Commands;

/// <summary>
/// Conversation flow: "Hey Elmer" wakes the assistant ("Aye Captain") and it stays awake and interactive
/// until "Elmer, dismissed". Understood commands are confirmed with "Aye Aye" and executed.
/// A wake word and command in one utterance does both in order.
/// </summary>
public sealed class VoiceSession
{
    private static readonly System.Text.RegularExpressions.Regex Dismissal = new(
        @"\b(dismissed?|stand down)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static readonly System.Text.RegularExpressions.Regex Shutdown = new(
        @"^\W*(shut ?down|power down|go offline|quit)\W*(elmer|yourself|assistant|completely|entirely)?\W*$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static readonly System.Text.RegularExpressions.Regex SelfEcho = new(
        @"^\W*(aye,? captain|aye,? aye|sorry,? captain|ay,? captain|pardon)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private readonly CommandExecutor _executor;
    private readonly Action<string> _say;
    private readonly Func<DateTime> _now;
    private readonly IConversationModel? _model;
    private bool _awake;

    public VoiceSession(CommandExecutor executor, Action<string> say, Func<DateTime>? now = null, IConversationModel? model = null)
    {
        _executor = executor;
        _say = text =>
        {
            _spoken.Add(text);
            if (_spoken.Count > 5)
            {
                _spoken.RemoveAt(0);
            }

            say(text);
        };
        _now = now ?? (() => DateTime.UtcNow);
        _model = model;
    }

    public const string UnclearMarker = "<unclear>";

    private readonly List<DateTime> _modelCalls = [];
    private readonly List<string> _spoken = [];

    private static string[] Words(string s) =>
        System.Text.RegularExpressions.Regex.Matches(s.ToLowerInvariant(), "[a-z0-9']+").Select(m => m.Value).ToArray();

    // True when most of what was heard is words Elmer itself just said.
    private bool MatchesRecentSpeech(string transcript)
    {
        var heard = Words(transcript);
        if (heard.Length < 4)
        {
            return false;
        }

        var said = new HashSet<string>(_spoken.SelectMany(Words));
        return heard.Count(said.Contains) >= heard.Length * 0.8;
    }

    public bool IsListening => _awake;

    private static bool FastCommands =>
        string.Equals(Environment.GetEnvironmentVariable("ELMER_FAST_COMMANDS"), "on", StringComparison.OrdinalIgnoreCase);

    /// <returns>"PASS", "LISTENING", or "HANDLED ..." for a line-based host to relay.</returns>
    public async Task<string> HandleAsync(string transcript, CancellationToken ct = default)
    {
        var wasListening = _awake;

        // The listener heard speech it couldn't transcribe; just keep listening.
        if (transcript.Trim() == UnclearMarker)
        {
            return wasListening ? "LISTENING" : "PASS";
        }

        // Elmer's own speech can be picked up by the mic; never answer it.
        if (SelfEcho.IsMatch(transcript) || MatchesRecentSpeech(transcript))
        {
            return wasListening ? "LISTENING" : "PASS";
        }

        var parsed = CommandParser.Parse(transcript);
        if (Dismissal.IsMatch(transcript) && (wasListening || parsed.Status != ParseStatus.NotAddressed))
        {
            _awake = false;
            return "HANDLED dismissed";
        }

        // A whole-utterance "shut down" only counts when Elmer is awake or addressed by name.
        if ((wasListening || parsed.Status != ParseStatus.NotAddressed)
            && Shutdown.IsMatch(CommandParser.StripWakeWord(transcript)))
        {
            _awake = false;
            _say("Aye Captain, shutting down.");
            return "HANDLED shutdown";
        }

        if (parsed.Status == ParseStatus.NotAddressed && wasListening)
        {
            parsed = CommandParser.ParseCommand(transcript);
        }
        else if (parsed.Status != ParseStatus.NotAddressed)
        {
            _awake = true;
            _say("Aye Captain");
        }

        // With a model, the AI handles every request through the tools; the parser is only a fast path on request.
        if (_model is not null && !FastCommands
            && parsed.Status is ParseStatus.Unrecognized or ParseStatus.Invalid or ParseStatus.Command)
        {
            return await ConverseAsync(transcript, wasListening, ct).ConfigureAwait(false);
        }

        switch (parsed.Status)
        {
            case ParseStatus.NotAddressed:
                return "PASS";
            case ParseStatus.WakeOnly:
                return Listen();
            case ParseStatus.Unrecognized when _model is not null:
                return await ConverseAsync(transcript, wasListening, ct).ConfigureAwait(false);
            case ParseStatus.Unrecognized:
                _say("Pardon?");
                return Listen();
            case ParseStatus.Invalid:
                _say($"Sorry Captain. {parsed.Message}");
                return Listen();
        }

        _say("Aye Aye");
        try
        {
            var outcome = await _executor.ExecuteAsync(parsed.Command!, ct).ConfigureAwait(false);
            if (outcome.Spoken is { Length: > 0 } spoken)
            {
                _say(spoken);
            }

            return "HANDLED " + outcome.Message;
        }
        catch (CommandException ex)
        {
            _say($"Sorry Captain, {ex.Message}");
            return $"HANDLED error: {ex.Message}";
        }
        catch (Exception ex)
        {
            _say("Sorry Captain, that failed.");
            return $"HANDLED error: {ex.Message}";
        }
    }

    private async Task<string> ConverseAsync(string transcript, bool wasListening, CancellationToken ct)
    {
        // Drop the wake word (if any) so the model sees only what the Captain asked.
        var text = CommandParser.StripWakeWord(transcript);

        // Backstop against runaway loops: no more than 6 model calls per minute.
        var now = _now();
        _modelCalls.RemoveAll(t => now - t > TimeSpan.FromMinutes(1));
        if (_modelCalls.Count >= 6)
        {
            return Listen();
        }

        if (text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 2)
        {
            _say("Pardon?");
            return Listen();
        }

        _modelCalls.Add(now);
        var confirmed = false;
        _model!.ActionStarted = () =>
        {
            if (!confirmed)
            {
                confirmed = true;
                _say("Aye Aye");
            }
        };
        try
        {
            var reply = await _model!.RespondAsync(text, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(reply))
            {
                _say("Pardon?");
                return Listen();
            }

            _say(reply);
            return "CHAT " + reply;
        }
        catch (Exception ex)
        {
            _say("Sorry Captain, I couldn't think of an answer.");
            return $"CHAT error: {ex.Message}";
        }
    }

    private string Listen()
    {
        _awake = true;
        return "LISTENING";
    }
}
