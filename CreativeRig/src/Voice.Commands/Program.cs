using Voice.Commands;

// Usage:
//   Voice.Commands                 read transcripts from stdin (one per line); prints PASS, LISTENING or HANDLED <msg>
//   Voice.Commands --say "text"    handle a single transcript
//   flags: --dry-run (don't touch REAPER), --no-speech (don't speak replies)
var dryRun = args.Contains("--dry-run");
var speak = !args.Contains("--no-speech");
var sayIndex = Array.IndexOf(args, "--say");

using var voice = speak ? new SpeechOutput(Environment.GetEnvironmentVariable("VOICE_OUTPUT")) : null;

if (args.Contains("--devices"))
{
    Console.WriteLine(string.Join(Environment.NewLine, SpeechOutput.DeviceNames));
    return;
}

void WaitForSpeech() => voice?.WaitUntilDone();

await using var hub = dryRun ? null : new McpHub();
var executor = new CommandExecutor(hub);

// ELMER_CHAT=off disables conversation; ELMER_MODEL picks the Copilot model (default: Copilot's own).
IConversationModel? chat = hub is null || string.Equals(Environment.GetEnvironmentVariable("ELMER_CHAT"), "off", StringComparison.OrdinalIgnoreCase)
    ? null
    : new CopilotConversationModel(executor, hub, Environment.GetEnvironmentVariable("ELMER_MODEL"));
await using var chatLifetime = chat;
var session = new VoiceSession(executor, text => voice?.Say(text), model: chat);

if (sayIndex >= 0 && sayIndex + 1 < args.Length)
{
    Console.WriteLine(await session.HandleAsync(args[sayIndex + 1]));
    WaitForSpeech();
    return;
}

if (chat is CopilotConversationModel copilot)
{
    _ = copilot.WarmUpAsync();
}

string? line;
while ((line = Console.ReadLine()) is not null)
{
    var reply = await session.HandleAsync(line);
    // Finish speaking before replying so the host can drop audio captured while we talked.
    WaitForSpeech();
    Console.WriteLine(reply);
    Console.Out.Flush();
}

WaitForSpeech();
