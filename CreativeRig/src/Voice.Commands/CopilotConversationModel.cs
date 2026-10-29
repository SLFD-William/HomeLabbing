using GitHub.Copilot;
using Microsoft.Extensions.AI;

namespace Voice.Commands;

/// <summary>Conversation backed by the GitHub Copilot CLI, using the signed-in Copilot subscription.</summary>
public sealed class CopilotConversationModel : IConversationModel
{
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(90);

    private readonly CommandExecutor _executor;
    private readonly string? _model;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CopilotClient? _client;
    private CopilotSession? _session;

    public Action? ActionStarted { get; set; }

    public CopilotConversationModel(CommandExecutor executor, string? model)
    {
        _executor = executor;
        _model = model;
    }

    private async Task<CopilotSession> EnsureSessionAsync(CancellationToken ct)
    {
        if (_session is not null)
        {
            return _session;
        }

        _client = new CopilotClient(new CopilotClientOptions());
        await _client.StartAsync(ct).ConfigureAwait(false);

        var tools = ConversationTools.Create(_executor, () => ActionStarted);
        _session = await _client.CreateSessionAsync(new SessionConfig
        {
            Model = _model,
            Tools = tools.Cast<AIFunctionDeclaration>().ToList(),
            // Only Elmer's own tools: the Copilot agent's shell and file tools stay off.
            AvailableTools = tools.Select(t => t.Name).ToList(),
            SystemMessage = new SystemMessageConfig { Mode = SystemMessageMode.Replace, Content = ConversationTools.SystemPrompt },
            OnPermissionRequest = PermissionHandler.ApproveAll,
            EnableConfigDiscovery = false,
        }, ct).ConfigureAwait(false);
        return _session;
    }

    public async Task<string> RespondAsync(string userText, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var session = await EnsureSessionAsync(ct).ConfigureAwait(false);
            var reply = await session.SendAndWaitAsync(userText, ReplyTimeout, ct).ConfigureAwait(false);
            return reply?.Data?.Content?.Trim() ?? "";
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            await _session.DisposeAsync().ConfigureAwait(false);
        }

        if (_client is not null)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
        }
    }
}
