using System.Collections.Concurrent;
using Markdig;

namespace OpenRussiaRouter.WebApp.Services.Legal;

public class LegalDocumentService
{
    private readonly string _docsDirectory;
    private readonly MarkdownPipeline _pipeline;
    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> AllowedDocuments = new(StringComparer.OrdinalIgnoreCase)
    {
        "privacy-policy.md",
        "public-offer.md"
    };

    public LegalDocumentService(IWebHostEnvironment environment)
    {
        var baseDir = AppContext.BaseDirectory;
        var candidate = Path.Combine(baseDir, "docs", "mandatory");
        _docsDirectory = Directory.Exists(candidate)
            ? candidate
            : Path.Combine(environment.ContentRootPath, "docs", "mandatory");
        _pipeline = new MarkdownPipelineBuilder()
            .DisableHtml()
            .Build();
    }

    internal LegalDocumentService(string docsDirectory)
    {
        _docsDirectory = docsDirectory;
        _pipeline = new MarkdownPipelineBuilder()
            .DisableHtml()
            .Build();
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

            var markdown = File.ReadAllText(filePath);
            return Markdown.ToHtml(markdown, _pipeline);
        }) is { Length: > 0 } html ? html : null;
    }
}
