using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.WebApp.Email;
using OpenRussiaRouter.WebApp.Pages;
using OpenRussiaRouter.WebApp.Support;

namespace OpenRussiaRouter.WebApp.Pages;

/// <summary>
/// Public "Contact support" form. Available to everyone — anonymous visitors and signed-in
/// users alike. On submit it relays a plain-text email to the staff inbox through the existing
/// async email queue (Reply-To = the submitter), so staff answer straight from their mailbox.
/// No persistence: this is the email-relay support channel, not an in-app ticket store.
/// </summary>
[AllowAnonymous]
public class SupportModel : PageModelBase
{
    private readonly IEmailDispatchQueue _emailQueue;
    private readonly SupportSettings _settings;
    private readonly IStringLocalizer<SharedResource> _localizer;
    private readonly ILogger<SupportModel> _logger;

    public SupportModel(
        IEmailDispatchQueue emailQueue,
        IOptions<SupportSettings> settings,
        IStringLocalizer<SharedResource> localizer,
        ILogger<SupportModel> logger)
    {
        _emailQueue = emailQueue;
        _settings = settings.Value;
        _localizer = localizer;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool Sent { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>Telegram link to show as an alternative channel, or null when not configured.</summary>
    public string? TelegramUrl =>
        string.IsNullOrWhiteSpace(_settings.TelegramUrl) ? null : _settings.TelegramUrl;

    public void OnGet()
    {
        // Prefill the email for signed-in users so they don't retype it.
        if (CurrentUser is { Email.Length: > 0 } user)
            Input.Email = user.Email;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // Honeypot: a hidden field no human sees. If a bot fills it, silently accept and drop —
        // showing the same success screen so the bot gets no signal.
        if (!string.IsNullOrEmpty(Input.Website))
        {
            _logger.LogInformation("Support form honeypot triggered; submission dropped.");
            Sent = true;
            return Page();
        }

        if (!ModelState.IsValid)
            return Page();

        var inbox = _settings.Inbox;
        if (string.IsNullOrWhiteSpace(inbox))
        {
            // Misconfiguration — make sure ops sees it, but don't dead-end the user or reveal it.
            _logger.LogError(
                "Support inbox not configured (Support:Inbox); message from {Email} was not delivered.",
                Input.Email);
            Sent = true;
            return Page();
        }

        try
        {
            await _emailQueue.EnqueueAsync(
                EmailTemplates.SupportRequestEmail(inbox, Input.Email, Input.Subject, Input.Message, BuildMeta()));
            _logger.LogInformation("Support request queued from {Email}.", Input.Email);
            Sent = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue support request from {Email}.", Input.Email);
            ErrorMessage = _localizer["Support_Error"];
        }

        return Page();
    }

    /// <summary>Triage context appended to the email body (not shown on the form).</summary>
    private string BuildMeta()
    {
        var account = CurrentUser is { } user
            ? $"userId={user.UserId}, role={user.Role}"
            : "not signed in";
        var userAgent = Request.Headers.UserAgent.ToString();
        return $"Account: {account}\nUser-Agent: {userAgent}";
    }

    public class InputModel
    {
        [Required(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Validation_Required")]
        [EmailAddress(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Validation_Email")]
        public string Email { get; set; } = "";

        [Required(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Validation_Required")]
        [StringLength(200)]
        public string Subject { get; set; } = "";

        [Required(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Validation_Required")]
        [StringLength(5000)]
        public string Message { get; set; } = "";

        /// <summary>Honeypot — must stay empty. Hidden from humans via CSS/aria in the view.</summary>
        public string? Website { get; set; }
    }
}
