using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Email;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class BroadcastModel(EmailBroadcastService broadcast, ILogger<BroadcastModel> logger) : PageModelBase
{
    private readonly EmailBroadcastService _broadcast = broadcast;
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
        try
        {
            var count = await _broadcast.EnqueueBroadcastAsync(Input.Subject, Input.Body);
            SuccessMessage = $"Broadcast queued to {count} recipient(s). Delivery runs in the background.";
            Input = new BroadcastInput();
            ModelState.Clear();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to queue broadcast. Subject={Subject}", Input.Subject);
            ErrorMessage = "Failed to queue broadcast.";
        }
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
