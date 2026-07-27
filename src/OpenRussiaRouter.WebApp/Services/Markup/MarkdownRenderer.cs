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
    /// <summary>
    /// Strict pipeline: raw HTML is escaped by Markdig at parse time instead of being scrubbed
    /// afterwards by regex. The regex pass below is a scrubber, not an HTML parser, so it cannot
    /// be relied on against nested or malformed markup.
    /// </summary>
    private static readonly MarkdownPipeline StrictPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    private static readonly Regex ImageTagRegex =
        new(@"<img\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ImageSrcRegex =
        new(@"src\s*=\s*(?:""(?<url>[^""]*)""|'(?<url>[^']*)')", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

        var html = global::Markdig.Markdown.ToHtml(markdown, StrictPipeline);
        return Sanitize(html);
    }

    /// <summary>
    /// Like <see cref="Render"/>, but additionally escapes raw HTML at parse time and drops any
    /// <c>&lt;img&gt;</c> whose source is not under <paramref name="allowedImagePrefix"/>.
    ///
    /// Used for admin-authored tool content, where every legitimate image is one we host at
    /// <c>/tools/image/{uuid}</c>. A remote image would leak each visitor's IP and user agent to a
    /// third party, and breaks whenever that host goes away. Do not use this for the OpenRouter
    /// model descriptions — those may legitimately reference remote images.
    /// </summary>
    public static string RenderStrict(string? markdown, string allowedImagePrefix = "/tools/image/")
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var html = global::Markdig.Markdown.ToHtml(markdown, StrictPipeline);
        html = Sanitize(html);
        return StripForeignImages(html, allowedImagePrefix);
    }

    private static string StripForeignImages(string html, string allowedImagePrefix) =>
        ImageTagRegex.Replace(html, match =>
        {
            var src = ImageSrcRegex.Match(match.Value);
            var url = src.Success ? src.Groups["url"].Value.Trim() : string.Empty;
            return url.StartsWith(allowedImagePrefix, StringComparison.Ordinal) ? match.Value : string.Empty;
        });

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
