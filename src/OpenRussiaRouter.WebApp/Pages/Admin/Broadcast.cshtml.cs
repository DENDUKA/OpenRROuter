using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Email;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class BroadcastModel(
    EmailBroadcastService broadcast,
    IServiceScopeFactory scopeFactory,
    ILogger<BroadcastModel> logger) : PageModelBase
{
    private readonly EmailBroadcastService _broadcast = broadcast;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<BroadcastModel> _logger = logger;

    public int RecipientCount { get; set; }
    public bool ShowConfirm { get; set; }
    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }

    [BindProperty] public BroadcastInput Input { get; set; } = new();

    public async Task OnGetAsync() => await LoadRecipientCount();

    public async Task<IActionResult> OnPostPreviewAsync()
    {
        await LoadRecipientCount();
        if (!ModelState.IsValid)
            return Page();

        ShowConfirm = true;
        return Page();
    }

    public async Task<IActionResult> OnPostSendAsync()
    {
        if (!ModelState.IsValid)
        {
            await LoadRecipientCount();
            return Page();
        }

        var ct = HttpContext.RequestAborted;
        var subject = Input.Subject;
        var body = Input.Body;

        // Fire-and-forget: start the broadcast in a background task so the
        // HTTP request returns immediately instead of blocking for minutes
        // when the channel is full or the email transport is slow.
        // The cancellation token allows aborting when the client disconnects.
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var broadcastSvc = scope.ServiceProvider.GetRequiredService<EmailBroadcastService>();
                var count = await broadcastSvc.EnqueueBroadcastAsync(subject, body, ct);
                _logger.LogInformation("Broadcast '{Subject}' enqueued to {Count} recipients.", subject, count);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogWarning("Broadcast '{Subject}' cancelled (client disconnected).", subject);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enqueue broadcast. Subject={Subject}", subject);
            }
        }, ct);

        SuccessMessage = "Broadcast queued. Delivery runs in the background.";
        Input = new BroadcastInput();
        ModelState.Clear();
        await LoadRecipientCount();
        return Page();
    }

    private async Task LoadRecipientCount()
    {
        try
        {
            RecipientCount = await _broadcast.GetRecipientCountAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load broadcast recipient count.");
            RecipientCount = 0;
        }
    }

    public class BroadcastInput
    {
        [Required, StringLength(200)] public string Subject { get; set; } = "";
        [Required] public string Body { get; set; } = "";
    }
}
