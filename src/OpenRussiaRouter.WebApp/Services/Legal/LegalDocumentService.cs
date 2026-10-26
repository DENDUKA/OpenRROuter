using System.Collections.Concurrent;
using Markdig;

namespace OpenRussiaRouter.WebApp.Services.Legal;

public class LegalDocumentService
{
    private readonly string _docsDirectory;
    private readonly IReadOnlyDictionary<string, string> _templateValues;
    private readonly MarkdownPipeline _pipeline;
    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, string> DefaultTemplateValues =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["RWT_SERVICE_NAME"] = "Rawrter",
            ["RWT_DOCUMENT_DATE"] = "26.10.2026",
            ["RWT_PUBLIC_DOMAIN"] = "https://rawrter.com",
            ["RWT_OPERATOR_NAME"] = "Индивидуальный предприниматель Зоткин Денис Игоревич",
            ["RWT_OPERATOR_DETAILS"] = "ИНН: 645293322359, ОГРНИП: 323645700043305, официальный сайт: https://rawrter.com",
            ["RWT_SUPPORT_CONTACT"] = "электронная почта denduka@yandex.ru"
        };

    private static readonly HashSet<string> AllowedDocuments = new(StringComparer.OrdinalIgnoreCase)
    {
        "privacy-policy.md",
        "public-offer.md"
    };

    public LegalDocumentService(IWebHostEnvironment environment, IConfiguration configuration)
        : this(ResolveDocsDirectory(environment), BuildTemplateValues(configuration))
    {
    }

    internal LegalDocumentService(string docsDirectory)
        : this(docsDirectory, DefaultTemplateValues)
    {
    }

    private LegalDocumentService(
        string docsDirectory,
        IReadOnlyDictionary<string, string> templateValues)
    {
        _docsDirectory = docsDirectory;
        _templateValues = templateValues;
        _pipeline = new MarkdownPipelineBuilder()
            .DisableHtml()
            .Build();
    }

    private static string ResolveDocsDirectory(IWebHostEnvironment environment)
    {
        var candidate = Path.Combine(AppContext.BaseDirectory, "docs", "mandatory");
        return Directory.Exists(candidate)
            ? candidate
            : Path.Combine(environment.ContentRootPath, "docs", "mandatory");
    }

    private static IReadOnlyDictionary<string, string> BuildTemplateValues(IConfiguration configuration)
    {
        return DefaultTemplateValues.ToDictionary(
            pair => pair.Key,
            pair => configuration[$"Legal:{pair.Key}"]
                ?? configuration[pair.Key]
                ?? pair.Value,
            StringComparer.Ordinal);
    }

    private string ReplaceTemplateValues(string markdown)
    {
        foreach (var (name, value) in _templateValues)
            markdown = markdown.Replace($"{{{{{name}}}}}", value, StringComparison.Ordinal);

        return markdown;
    }

    public string? GetDocumentHtml(string documentName)
    {
        if (!AllowedDocuments.Contains(documentName))
            return null;

        return _cache.GetOrAdd(documentName, name =>
        {
            var filePath = Path.Combine(_docsDirectory, name);
            if (!File.Exists(filePath))
                return "";

            var markdown = ReplaceTemplateValues(File.ReadAllText(filePath));
            return Markdown.ToHtml(markdown, _pipeline);
        }) is { Length: > 0 } html ? html : null;
    }
}
