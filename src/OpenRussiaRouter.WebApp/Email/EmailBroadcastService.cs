using OpenRussiaRouter.WebApp.Services.Auth;

namespace OpenRussiaRouter.WebApp.Email;

/// <summary>
/// Builds a per-user broadcast and hands the batch to <see cref="IEmailDispatchQueue"/>;
/// the dispatch worker does the throttled sending. Recipients come from AuthService —
/// every user has an email by construction (invite-based registration), so the only
/// filtering is dropping rows with a missing/blank address.
/// </summary>
public sealed class EmailBroadcastService
{
    private readonly AuthProvider _authProvider;
    private readonly IEmailDispatchQueue _queue;
    private readonly ILogger<EmailBroadcastService> _logger;

    public EmailBroadcastService(
        AuthProvider authProvider,
        IEmailDispatchQueue queue,
        ILogger<EmailBroadcastService> logger)
    {
        _authProvider = authProvider;
        _queue = queue;
        _logger = logger;
    }

    /// <summary>Number of users that would receive a broadcast right now.</summary>
    public async Task<int> GetRecipientCountAsync(CancellationToken ct = default)
    {
        var users = await _authProvider.ListUsersAsync(ct);
        return BuildMessages(users, subject: "x", body: "x").Count;
    }

    /// <summary>
    /// Fetches all users, builds one <see cref="EmailMessage"/> each, enqueues the batch,
    /// and returns how many were enqueued.
    /// </summary>
    public async Task<int> EnqueueBroadcastAsync(string subject, string body, CancellationToken ct = default)
    {
        var users = await _authProvider.ListUsersAsync(ct);
        var messages = BuildMessages(users, subject, body);
        if (messages.Count > 0)
            await _queue.EnqueueBatchAsync(messages, ct);
        _logger.LogInformation("Broadcast '{Subject}' enqueued to {Count} recipients.", subject, messages.Count);
        return messages.Count;
    }

    /// <summary>
    /// Pure mapping users → messages (skips blank addresses). Exposed for unit tests so the
    /// fan-out logic can be exercised without a live AuthService.
    /// </summary>
    public static IReadOnlyList<EmailMessage> BuildMessages(
        IEnumerable<UserInfo> users, string subject, string body)
        => users
            .Where(u => !string.IsNullOrWhiteSpace(u.Email))
            .Select(u => new EmailMessage(u.Email, subject, body))
            .ToList();
}
