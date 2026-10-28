using System.Text.RegularExpressions;

namespace Voice.Commands;

public enum ParseStatus
{
    /// <summary>The wake word was not spoken; the transcript is not for this assistant.</summary>
    NotAddressed,

    /// <summary>Only the wake word was spoken; the command should follow in the next utterance.</summary>
    WakeOnly,

    /// <summary>Addressed, but no known command matched.</summary>
    Unrecognized,

    /// <summary>Addressed, command recognised but its arguments are unusable.</summary>
    Invalid,

    Command,
}

public sealed record ParseResult(ParseStatus Status, VoiceCommand? Command = null, string? Message = null);

/// <summary>
/// Rule-based parser, e.g. "Hey Elmer, set the project tempo in Reaper to 171 beats per minute".
/// Rules run in order and the first match wins; keep more specific rules ahead of generic ones.
/// </summary>
public static partial class CommandParser
{
    public const double MaxBpm = 960;

    // Below ~20 BPM is almost always a mis-heard number (e.g. "171" heard as "1").
    public const double MinBpm = 20;

    private static readonly string[] Moods = ["dreamy", "aggressive", "warm", "spacious", "clean"];

    // Whisper spells the name a few ways; "hey" is optional.
    [GeneratedRegex(@"(?:(?:hey|hay|hi|ok|okay)\W+)?(?:elmer|elmar|elmore|ellmer|almer|el\s+mer|el\s+more)\b\W*", RegexOptions.IgnoreCase)]
    private static partial Regex WakeRegex();

    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly Regex ObsSpelling = new(@"\bo\.?\s?b\.?\s?s\b\.?", Opts);
    private static readonly Regex CapCutSpelling = new(@"\bcap\s*cut(?:'s)?\b", Opts);
    private static readonly Regex HeadrushSpelling = new(@"\bhead\s*rush(?:'s)?\b", Opts);
    private static readonly Regex GForceSpelling = new(@"\bg[\s-]*force\b", Opts);
    private static readonly Regex VsCodeSpelling = new(@"\b(?:vs|v\s*s|visual\s+studio)\s*code\b", Opts);
    private static readonly Regex Apostrophes = new(@"['\u2019]", Opts);
    private static readonly Regex Noise = new(@"[^A-Za-z0-9.\s]", Opts);
    private static readonly Regex Filler = new(@"\b(?:please|kindly)\b", Opts);
    private static readonly Regex Spaces = new(@"\s+", Opts);

    private static readonly Regex AddTextRe = new(@"\badd\s+(?:the\s+)?text\s+(.+?)\s+(?:to|in|on)\s+(?:the\s+)?capcut(?:\s+draft)?\s+(.+)$", Opts);
    private static readonly Regex CalledRe = new(@"\b(?:called|named)\s+(.+)$", Opts);
    private static readonly Regex DraftNameRe = new(@"\b(?:draft|project)\s+(?:called\s+|named\s+)?(.+)$", Opts);

    private static readonly Regex BackupRe = new(@"\bback\s?up\b", Opts);
    private static readonly Regex LoadRigRe1 = new(@"\b(?:load|switch to|select|use|change to|go to)\s+(?:the\s+)?(?:rig|preset|patch)\s+(.+?)(?:\s+(?:on|in|to)\s+(?:the\s+)?headrush)?$", Opts);
    private static readonly Regex LoadRigRe2 = new(@"\b(?:load|switch to|select|use|change to|go to)\s+(?:the\s+)?(.+?)\s+(?:rig|preset|patch)(?:\s+(?:on|in|to)\s+(?:the\s+)?headrush)?$", Opts);

    private static readonly Regex SceneToRe = new(@"\bto\s+(?:the\s+)?(.+?)\s+scene\b", Opts);
    private static readonly Regex SceneNameRe = new(@"\bscene\s+(?:called\s+|named\s+)?(.+?)(?:\s+(?:in|on)\s+obs)?$", Opts);
    private static readonly Regex SourceRe = new(@"\b(show|hide|enable|disable)\s+(?:the\s+)?(.+?)(?:\s+source)?(?:\s+(?:in|on|from)\s+obs)?$", Opts);
    private static readonly Regex MuteRe = new(@"\b(unmute|mute)\s+(?:the\s+)?(.+?)(?:\s+(?:in|on)\s+obs)?$", Opts);
    private static readonly Regex GoToStartRe = new(@"\b(?:go to|back to|return to|rewind to)\s+(?:the\s+)?(?:start|beginning|top)\b", Opts);

    private sealed record Ctx(string Text, List<string> Tokens)
    {
        public bool Has(params string[] words) => words.Any(Tokens.Contains);

        public bool HasPhrase(string phrase) => Text.Contains(phrase, StringComparison.OrdinalIgnoreCase);
    }

    private static readonly Func<Ctx, ParseResult?>[] Rules =
    [
        StudioRule,
        CapCutRule,
        HeadrushRule,
        TempoRule,
        StreamRule,
        SceneRule,
        SourceRule,
        TrackRule,
        ObsMuteRule,
        RecordingRule,
        MoodRule,
        RenderRule,
        TransportRule,
        StopRule,
    ];

    /// <summary>Parses a full utterance containing the wake word.</summary>
    public static ParseResult Parse(string transcript)
    {
        // Speech often has chatter before the wake word, and Whisper may repeat it; use the last one.
        var wake = WakeRegex().Matches(transcript ?? "").LastOrDefault();
        if (wake is null)
        {
            return new ParseResult(ParseStatus.NotAddressed);
        }

        var result = ParseCommand(transcript![(wake.Index + wake.Length)..]);
        return result.Status == ParseStatus.Unrecognized && string.IsNullOrWhiteSpace(result.Message?.Trim('.', ' '))
            ? new ParseResult(ParseStatus.WakeOnly)
            : result;
    }

    /// <summary>Returns the transcript after the last wake word, or the whole transcript if there is none.</summary>
    public static string StripWakeWord(string transcript)
    {
        var wake = WakeRegex().Matches(transcript ?? "").LastOrDefault();
        return wake is null ? transcript ?? "" : transcript![(wake.Index + wake.Length)..].Trim();
    }

    /// <summary>Parses command text with the wake word already consumed.</summary>
    public static ParseResult ParseCommand(string commandText)
    {
        var words = Normalize(commandText).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim('.'))
            .Where(t => t.Length > 0)
            .ToList();
        var ctx = new Ctx(string.Join(' ', words), words.Select(w => w.ToLowerInvariant()).ToList());

        foreach (var rule in Rules)
        {
            if (rule(ctx) is { } result)
            {
                return result;
            }
        }

        return new ParseResult(ParseStatus.Unrecognized, Message: ctx.Text);
    }

    private static string Normalize(string s)
    {
        s = ObsSpelling.Replace(s ?? "", "obs");
        s = CapCutSpelling.Replace(s, "capcut");
        s = HeadrushSpelling.Replace(s, "headrush");
        s = GForceSpelling.Replace(s, "gforce");
        s = VsCodeSpelling.Replace(s, "vscode");
        s = Apostrophes.Replace(s, "");
        s = Noise.Replace(s, " ");
        s = Filler.Replace(s, " ");
        return Spaces.Replace(s, " ").Trim();
    }

    private static ParseResult Cmd(VoiceCommand c) => new(ParseStatus.Command, c);

    private static ParseResult Invalid(string message) => new(ParseStatus.Invalid, Message: message);

    private static string Name(Group g) => g.Value.Trim(' ', '.');

    // ---- Studio ----------------------------------------------------------------------------

    // Short phrases only ("launch studio", "kill the studio"): they run the launch/close bat files.
    private static ParseResult? StudioRule(Ctx c)
    {
        if (c.Tokens.Count > 5)
        {
            return null;
        }

        var launch = c.Has("launch", "start", "open", "boot", "fire", "run");
        var close = c.Has("kill", "close", "quit", "exit", "shutdown") || c.HasPhrase("shut down");

        // "start recording in OBS" and similar are not app launches.
        if (!c.Has("record", "recording", "stream", "streaming", "scene", "tempo")
            && (launch || close)
            && AppOf(c) is { } app)
        {
            return Cmd(launch ? new LaunchApp(app) : new CloseApp(app));
        }

        if (!c.Has("studio"))
        {
            return null;
        }

        if (launch)
        {
            return Cmd(new LaunchStudio());
        }

        return close || c.Has("stop", "end") ? Cmd(new CloseStudio()) : null;
    }

    private static StudioApp? AppOf(Ctx c) =>
        c.Has("reaper") ? StudioApp.Reaper
        : c.Has("obs") ? StudioApp.Obs
        : c.Has("headrush") ? StudioApp.Headrush
        : c.Has("gforce") ? StudioApp.GForce
        : c.Has("vscode") ? StudioApp.VsCode
        : null;

    // ---- CapCut ----------------------------------------------------------------------------

    private static ParseResult? CapCutRule(Ctx c)
    {
        if (!c.Has("capcut"))
        {
            return null;
        }

        if (c.Has("list", "drafts"))
        {
            return Cmd(new CapCutList());
        }

        if (AddTextRe.Match(c.Text) is { Success: true } add)
        {
            return Cmd(new CapCutAddText(Name(add.Groups[2]), Name(add.Groups[1])));
        }

        if (c.Has("save"))
        {
            return DraftNameRe.Match(c.Text) is { Success: true } m && Name(m.Groups[1]).Length > 0
                ? Cmd(new CapCutSave(Name(m.Groups[1])))
                : Invalid("Which CapCut draft should I save?");
        }

        if (c.Has("create", "make", "new"))
        {
            var m = CalledRe.Match(c.Text);
            if (!m.Success)
            {
                m = DraftNameRe.Match(c.Text);
            }

            return m.Success && Name(m.Groups[1]).Length > 0
                ? Cmd(new CapCutCreate(Name(m.Groups[1])))
                : Invalid("What should the CapCut draft be called?");
        }

        return new ParseResult(ParseStatus.Unrecognized, Message: c.Text);
    }

    // ---- HeadRush --------------------------------------------------------------------------

    private static ParseResult? HeadrushRule(Ctx c)
    {
        if (!c.Has("headrush", "rig", "rigs"))
        {
            return null;
        }

        if (BackupRe.IsMatch(c.Text))
        {
            return Cmd(new HeadrushBackup());
        }

        if (c.Has("what", "whats", "which") && !c.Has("load", "switch", "select"))
        {
            return Cmd(new HeadrushWhichRig());
        }

        var m = LoadRigRe1.Match(c.Text);
        if (!m.Success)
        {
            m = LoadRigRe2.Match(c.Text);
        }

        if (m.Success && Name(m.Groups[1]).Length > 0)
        {
            return Cmd(new HeadrushLoadRig(Name(m.Groups[1])));
        }

        return c.Has("headrush") ? new ParseResult(ParseStatus.Unrecognized, Message: c.Text) : null;
    }

    // ---- REAPER tempo ----------------------------------------------------------------------

    private static ParseResult? TempoRule(Ctx c)
    {
        if (!(c.Has("tempo", "bpm") || c.HasPhrase("beats per minute")))
        {
            return null;
        }

        var hasNumber = NumberParser.TryFind(c.Tokens, 0, out _);
        if (c.Has("what", "whats", "which", "current", "read") && !hasNumber)
        {
            // Longer questions ("what's a good tempo for a slow blues") are for the conversation model.
            return c.Tokens.Count <= 6 ? Cmd(new GetTempo()) : null;
        }

        // Prefer the number after "to" ("set tempo to 171") so "in reaper 2" style noise is ignored.
        var from = c.Tokens.LastIndexOf("to");
        if (from < 0)
        {
            from = Math.Max(c.Tokens.IndexOf("tempo"), 0);
        }

        if (!NumberParser.TryFind(c.Tokens, from, out var bpm) && !NumberParser.TryFind(c.Tokens, 0, out bpm))
        {
            return Invalid("I didn't catch the tempo.");
        }

        return bpm is < MinBpm or > MaxBpm
            ? Invalid($"{bpm:0.##} BPM is outside the {MinBpm}-{MaxBpm} range.")
            : Cmd(new SetTempo(bpm));
    }

    // ---- OBS -------------------------------------------------------------------------------

    private static ParseResult? StreamRule(Ctx c)
    {
        var goLive = c.HasPhrase("go live");
        if (!(c.Has("stream", "streaming") || goLive))
        {
            return null;
        }

        if (c.Has("stop", "end", "halt", "kill"))
        {
            return Cmd(new ObsStream(false));
        }

        return c.Has("start", "begin", "launch") || goLive ? Cmd(new ObsStream(true)) : null;
    }

    private static ParseResult? SceneRule(Ctx c)
    {
        if (!c.Has("scene") || !c.Has("switch", "go", "change", "cut", "set", "select", "load", "activate", "use"))
        {
            return null;
        }

        var m = SceneToRe.Match(c.Text);
        if (!m.Success)
        {
            m = SceneNameRe.Match(c.Text);
        }

        var name = m.Success ? Name(m.Groups[1]) : "";
        return name.Length > 0 && name.ToLowerInvariant() != "obs" ? Cmd(new ObsScene(name)) : null;
    }

    private static ParseResult? SourceRule(Ctx c)
    {
        if (!(c.Has("obs") || c.Has("source")))
        {
            return null;
        }

        var m = SourceRe.Match(c.Text);
        if (!m.Success || Name(m.Groups[2]).Length == 0)
        {
            return null;
        }

        var visible = m.Groups[1].Value.ToLowerInvariant() is "show" or "enable";
        return Cmd(new ObsSource(Name(m.Groups[2]), visible));
    }

    private static ParseResult? ObsMuteRule(Ctx c)
    {
        if (!c.Has("obs") || !c.Has("mute", "unmute"))
        {
            return null;
        }

        var m = MuteRe.Match(c.Text);
        return m.Success && Name(m.Groups[2]).Length > 0
            ? Cmd(new ObsMute(Name(m.Groups[2]), m.Groups[1].Value.Equals("mute", StringComparison.OrdinalIgnoreCase)))
            : null;
    }

    // ---- REAPER ----------------------------------------------------------------------------

    private static ParseResult? TrackRule(Ctx c)
    {
        if (!c.Has("track") || !c.Has("mute", "unmute", "solo", "unsolo"))
        {
            return null;
        }

        if (TrackNumber(c.Tokens) is not { } track)
        {
            return Invalid("Which track? Say the track number.");
        }

        var action = c.Has("unmute") ? TrackAction.Unmute
            : c.Has("mute") ? TrackAction.Mute
            : c.Has("unsolo") ? TrackAction.Unsolo
            : TrackAction.Solo;
        return Cmd(new TrackState(action, track));
    }

    private static ParseResult? RecordingRule(Ctx c)
    {
        if (!c.Has("record", "recording"))
        {
            return null;
        }

        var target = c.Has("obs") ? Target.Obs : Target.Reaper;
        return c.Has("stop", "end", "finish")
            ? Cmd(new StopRecording(target))
            : Cmd(new StartRecording(target));
    }

    private static ParseResult? MoodRule(Ctx c)
    {
        if (!c.Has("apply", "add", "make", "give", "put") || Moods.FirstOrDefault(c.Tokens.Contains) is not { } mood)
        {
            return null;
        }

        return TrackNumber(c.Tokens) is { } track
            ? Cmd(new ApplyMood(mood, track))
            : Invalid("Which track? Say the track number.");
    }

    private static ParseResult? RenderRule(Ctx c)
    {
        // Render is slow and irreversible: only short, command-like phrases ("render the project").
        if (c.Has("render", "bounce"))
        {
            return c.Tokens.Count <= 5 ? Cmd(new RenderProject()) : null;
        }

        return c.Has("analyze", "analyse") && c.Has("audio", "item", "region", "selection", "selected", "clip")
            ? Cmd(new AnalyzeAudio())
            : null;
    }

    private static ParseResult? TransportRule(Ctx c)
    {
        if (c.Has("metronome", "click"))
        {
            return Cmd(new Transport(c.Has("off", "disable") ? TransportAction.MetronomeOff
                : c.Has("on", "enable") ? TransportAction.MetronomeOn
                : TransportAction.MetronomeToggle));
        }

        if (c.Has("play", "playback"))
        {
            return Cmd(new Transport(TransportAction.Play));
        }

        if (c.Has("pause"))
        {
            return Cmd(new Transport(TransportAction.Pause));
        }

        if (c.Has("rewind") || GoToStartRe.IsMatch(c.Text))
        {
            return Cmd(new Transport(TransportAction.GoToStart));
        }

        if (c.Has("save") && c.Has("project", "session", "reaper"))
        {
            return Cmd(new Transport(TransportAction.Save));
        }

        if (c.Has("undo"))
        {
            return Cmd(new Transport(TransportAction.Undo));
        }

        return c.Has("redo") ? Cmd(new Transport(TransportAction.Redo)) : null;
    }

    private static ParseResult? StopRule(Ctx c) => c.Has("stop") ? Cmd(new StopRecording()) : null;

    /// <summary>The number after "track"; Whisper often hears "two"/"four" as "to"/"for".</summary>
    private static int? TrackNumber(List<string> tokens)
    {
        var i = tokens.IndexOf("track");
        if (i < 0 || i + 1 >= tokens.Count)
        {
            return null;
        }

        var after = tokens.Skip(i + 1).Take(3).ToList();
        after[0] = after[0] switch
        {
            "to" or "too" => "two",
            "for" => "four",
            "won" => "one",
            _ => after[0],
        };

        return NumberParser.TryFind(after, 0, out var n) && n is >= 1 and <= 999 && n == Math.Floor(n) ? (int)n : null;
    }
}
