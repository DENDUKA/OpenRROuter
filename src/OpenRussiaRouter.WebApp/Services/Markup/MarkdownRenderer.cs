using System.Text.RegularExpressions;
using Markdig;

namespace OpenRussiaRouter.WebApp.Services.Markup;

/// <summary>
/// Renders untrusted Markdown to a sanitized HTML subset.
/// Used by the public model details page where the description is sourced from
/// OpenRouter (read-only for unauthenticated visitors) and from admin-editable
/// textareas (trusted users, but we still sanitize as defense-in-depth).
/// </summary>
public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    /// <summary>
    /// Convert markdown to sanitized HTML. Returns empty string for null/whitespace input.
    /// Disallowed constructs: <c>&lt;script&gt;</c>, <c>&lt;iframe&gt;</c>, inline event handlers,
    /// <c>javascript:</c>/<c>data:</c> URLs, <c>style</c> attributes, and any element that can load
    /// remote content or run scripts. Allowed tags cover structural markdown output:
    /// headings, paragraphs, lists, blockquotes, code, tables, links, images, emphasis, hr.
    /// </summary>
    public static string Render(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var html = global::Markdig.Markdown.ToHtml(markdown, Pipeline);
        return Sanitize(html);
    }

    private static string Sanitize(string html)
    {
        html = Regex.Replace(html, @"<\s*(script|style|iframe|object|embed|form|noscript|template|base|link|meta)\b[^>]*>.*?<\s*/\s*\1\s*>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        html = Regex.Replace(html, @"<\s*(script|style|iframe|object|embed|form|noscript|template|base|link|meta)\b[^>]*/?>", string.Empty, RegexOptions.IgnoreCase);

        html = Regex.Replace(html, @"\s(on[a-z]+|style)\s*=\s*""[^""]*""", string.Empty, RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"\s(on[a-z]+|style)\s*=\s*'[^']*'", string.Empty, RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"\s(on[a-z]+|style)\s*=\s*[^\s>]+", string.Empty, RegexOptions.IgnoreCase);

        html = Regex.Replace(html, @"(href|src|formaction|xlink:href)\s*=\s*""\s*(?:javascript|vbscript|data)\s*:[^""]*""", "$1=\"#\"", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"(href|src|formaction|xlink:href)\s*=\s*'\s*(?:javascript|vbscript|data)\s*:[^']*'", "$1='#'", RegexOptions.IgnoreCase);

        html = Regex.Replace(html, @"(<a\s)([^>]*href=""https?://[^""]+""[^>]*)>", "$1$2 rel=\"noopener noreferrer nofollow\" target=\"_blank\">", RegexOptions.IgnoreCase);

        return html.Trim();
    }
}
