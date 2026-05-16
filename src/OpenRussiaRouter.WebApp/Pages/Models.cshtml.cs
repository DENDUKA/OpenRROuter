using OpenRussiaRouter.WebApp.Services.Billing;

namespace OpenRussiaRouter.WebApp.Pages;

public class ModelsModel : PageModelBase
{
    private readonly BillingClient _billingClient;

    public IReadOnlyList<ModelInfo> Models { get; set; } = [];
    public string? ErrorMessage { get; set; }

    public static readonly string[] Categories = ["All", "OpenAI", "Anthropic", "Google", "Meta", "Coding"];

    public ModelsModel(BillingClient billingClient) => _billingClient = billingClient;

    public async Task OnGetAsync()
    {
        try { Models = await _billingClient.ListPublicModelsAsync(); }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    public static string GetProvider(ModelInfo model)
    {
        var text = $"{model.Id} {model.DisplayName}";
        if (text.Contains("claude", StringComparison.OrdinalIgnoreCase) || text.Contains("anthropic", StringComparison.OrdinalIgnoreCase)) return "Anthropic";
        if (text.Contains("gpt", StringComparison.OrdinalIgnoreCase) || text.Contains("openai", StringComparison.OrdinalIgnoreCase)) return "OpenAI";
        if (text.Contains("gemini", StringComparison.OrdinalIgnoreCase) || text.Contains("google", StringComparison.OrdinalIgnoreCase)) return "Google";
        if (text.Contains("llama", StringComparison.OrdinalIgnoreCase) || text.Contains("meta", StringComparison.OrdinalIgnoreCase)) return "Meta";
        if (model.Id.Contains('/', StringComparison.Ordinal)) return model.Id.Split('/')[0];
        return "Model";
    }

    public static string GetCapability(ModelInfo model)
    {
        var text = $"{model.Id} {model.DisplayName}";
        if (text.Contains("vision", StringComparison.OrdinalIgnoreCase)) return "Vision";
        if (text.Contains("code", StringComparison.OrdinalIgnoreCase) || text.Contains("coder", StringComparison.OrdinalIgnoreCase)) return "Coding";
        if (text.Contains("reason", StringComparison.OrdinalIgnoreCase) || text.Contains("thinking", StringComparison.OrdinalIgnoreCase)) return "Reasoning";
        return "Text generation";
    }

    public static string GetInitials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return "AI";
        return string.Concat(parts.Take(2).Select(p => char.ToUpperInvariant(p[0])));
    }
}
