using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.WebApp.Pages;
using OpenRussiaRouter.WebApp;
using OpenRussiaRouter.WebApp.Services.Analytics;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.Payments;
using OpenRussiaRouter.WebApp.Services.State;

namespace OpenRussiaRouter.WebApp.Pages.Pay;

[Authorize]
public class MockModel : PageModelBase
{
    private readonly BillingProvider _billing;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IOptions<PaymentSettings> _settings;
    private readonly IWebHostEnvironment _env;
    private readonly IStringLocalizer<SharedResource> _localizer;
    private readonly ILogger<MockModel> _logger;

    public MockModel(
        BillingProvider billing,
        IHttpClientFactory httpFactory,
        IOptions<PaymentSettings> settings,
        IWebHostEnvironment env,
        IStringLocalizer<SharedResource> localizer,
        ILogger<MockModel> logger)
    {
        _billing = billing;
        _httpFactory = httpFactory;
        _settings = settings;
        _env = env;
        _localizer = localizer;
        _logger = logger;
    }

    /// <summary>Route segment: internal payment id (= order id).</summary>
    public string PaymentId { get; set; } = "";

    public PaymentInfo? Payment { get; private set; }
    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(string paymentId)
    {
        if (!_env.IsDevelopment()) return NotFound();

        PaymentId = paymentId;

        try
        {
            Payment = await GetOwnedPaymentAsync(paymentId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load payment {PaymentId}", paymentId);
        }

        if (Payment == null) ErrorMessage = _localizer["Mock_NotFound"];
        return Page();
    }

    public async Task<IActionResult> OnPostPayAsync(string paymentId)
    {
        if (!_env.IsDevelopment()) return NotFound();

        PaymentId = paymentId;

        PaymentInfo? payment;
        try { payment = await GetOwnedPaymentAsync(paymentId); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetPayment failed for {PaymentId}", paymentId);
            ErrorMessage = _localizer["Mock_NotFound"];
            return Page();
        }

        if (payment == null)
        {
            return NotFound();
        }

        var payload = MockPaymentProvider.BuildWebhookPayload(
            providerPaymentId: payment.ProviderPaymentId!,
            orderId: paymentId,
            eventType: "paid",
            amount: payment.ProviderAmount,
            currency: payment.ProviderCurrency,
            providerEventId: Guid.NewGuid().ToString("N"));

        try
        {
            await SendSelfWebhookAsync(payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Self-webhook (paid) failed for payment {PaymentId}", paymentId);
            Payment = payment;
            ErrorMessage = _localizer["Mock_CheckoutFailed"];
            return Page();
        }

        // The dev mock returns here instead of /workspace/credits?topup=success, so it has to
        // queue the goal itself — otherwise the paid path is untestable outside production.
        AnalyticsTempData.Enqueue(TempData, AnalyticsGoal.Valued(
            AnalyticsGoals.TopUpCompleted, payment.ProviderAmount, payment.ProviderCurrency));

        return RedirectToPage("/Workspace/PaymentSuccess");
    }

    public async Task<IActionResult> OnPostCancelAsync(string paymentId)
    {
        if (!_env.IsDevelopment()) return NotFound();

        PaymentInfo? payment = null;
        try { payment = await GetOwnedPaymentAsync(paymentId); }
        catch (Exception ex) { _logger.LogWarning(ex, "GetPayment failed for cancel {PaymentId}", paymentId); }

        if (payment == null)
        {
            return NotFound();
        }

        var payload = MockPaymentProvider.BuildWebhookPayload(
            providerPaymentId: payment.ProviderPaymentId!,
            orderId: paymentId,
            eventType: "canceled",
            amount: payment.ProviderAmount,
            currency: payment.ProviderCurrency,
            reason: "User canceled");

        try
        {
            await SendSelfWebhookAsync(payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cancel self-webhook failed for {PaymentId}", paymentId);
            Payment = payment;
            ErrorMessage = _localizer["Mock_CheckoutFailed"];
            return Page();
        }

        return RedirectToPage("/Workspace/Credits");
    }

    private async Task<PaymentInfo?> GetOwnedPaymentAsync(string paymentId)
    {
        var payment = await _billing.GetPaymentAsync(paymentId, HttpContext.RequestAborted);
        return payment is { ProviderPaymentId: not null } &&
               string.Equals(payment.UserId, User.GetUserId(), StringComparison.Ordinal)
            ? payment
            : null;
    }

    private async Task SendSelfWebhookAsync(string payload)
    {
        var secret = _settings.Value.Mock.SharedSecret;
        var webhookUrl = $"{Request.Scheme}://{Request.Host}/webhook/mock";

        var client = _httpFactory.CreateClient();
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, webhookUrl) { Content = content };
        request.Headers.Add("X-Mock-Signature", secret);

        using var response = await client.SendAsync(request, HttpContext.RequestAborted);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            _logger.LogWarning(
                "Self-webhook to {Url} returned {Status}: {Body}",
                webhookUrl, (int)response.StatusCode, body);
            response.EnsureSuccessStatusCode();
        }
    }
}
