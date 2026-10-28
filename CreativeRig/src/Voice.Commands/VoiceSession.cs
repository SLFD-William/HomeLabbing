namespace Voice.Commands;

/// <summary>
/// Conversation flow: wake word -> "Aye Captain" and listen; command understood -> "Aye Aye" and execute.
/// A wake word and command in one utterance does both in order.
/// </summary>
public sealed class VoiceSession
{
    public static readonly TimeSpan ListenWindow =     TimeSpan.FromSeconds(25);
        public static readonly TimeSpan ChatWindow = TimeSpan.FromSeconds(45);

    private static readonly System.Text.RegularExpressions.Regex Dismissal = new(
        @"\b(dismissed?|stand down|that('s| is| will be) all|never ?mind)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static readonly System.Text.RegularExpressions.Regex SelfEcho = new(
        @"^\W*(aye,? captain|aye,? aye|sorry,? captain|ay,? captain)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private readonly CommandExecutor _executor;
    private readonly Action<string> _say;
    private readonly Func<DateTime> _now;
    private readonly IConversationModel? _model;
    private DateTime? _listeningUntil;

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

    public bool IsListening => _listeningUntil is { } until && _now() < until;

    /// <returns>"PASS", "LISTENING", or "HANDLED ..." for a line-based host to relay.</returns>
    public async Task<string> HandleAsync(string transcript, CancellationToken ct = default)
    {
        var wasListening = IsListening;
        var previousDeadline = _listeningUntil;
        _listeningUntil = null;

        // The listener heard speech it couldn't transcribe while a command was expected.
        if (transcript.Trim() == UnclearMarker)
        {
            if (!wasListening)
            {
                return "PASS";
            }

            _say("Sorry Captain, I didn't catch that. Could you say it again?");
            return Listen();
        }

        // Elmer's own speech can be picked up by the mic; never answer it or extend the window.
        if (SelfEcho.IsMatch(transcript) || MatchesRecentSpeech(transcript))
        {
            if (wasListening)
            {
                _listeningUntil = previousDeadline;
                return "LISTENING";
            }

            return "PASS";
        }

        var parsed = CommandParser.Parse(transcript);
        if (Dismissal.IsMatch(transcript) && (wasListening || parsed.Status != ParseStatus.NotAddressed))
        {
            return "HANDLED dismissed";
        }

        if (parsed.Status == ParseStatus.NotAddressed && wasListening)
        {
            parsed = CommandParser.ParseCommand(transcript);
        }
        else if (parsed.Status != ParseStatus.NotAddressed)
        {
            _say("Aye Captain");
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
                _say("Sorry Captain, I didn't understand that.");
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
        if (_modelCalls.Count >= 6 || text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 2)
        {
            _say("Sorry Captain, I didn't catch that. Could you say it again?");
            return Listen();
        }

        _modelCalls.Add(now);
        try
        {
            var reply = await _model!.RespondAsync(text, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(reply))
            {
                _say("Sorry Captain, I didn't understand that.");
                return Listen();
            }

            _say(reply);
            Listen(ChatWindow);
            return "CHAT " + reply;
        }
        catch (Exception ex)
        {
            _say("Sorry Captain, I couldn't think of an answer.");
            Listen(ChatWindow);
            return $"CHAT error: {ex.Message}";
        }
    }

    private string Listen(TimeSpan? window = null)
    {
        _listeningUntil = _now() + (window ?? ListenWindow);
        return "LISTENING";
    }
}
