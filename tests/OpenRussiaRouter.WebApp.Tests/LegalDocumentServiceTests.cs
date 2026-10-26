using OpenRussiaRouter.WebApp.Services.Legal;

namespace OpenRussiaRouter.WebApp.Tests;

public class LegalDocumentServiceTests : IDisposable
{
    private readonly string _testDocsDirectory;
    private readonly LegalDocumentService _service;

    public LegalDocumentServiceTests()
    {
        _testDocsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDocsDirectory);

        _service = new LegalDocumentService(_testDocsDirectory);
    }

    [Fact]
    public void GetDocumentHtml_PrivacyPolicy_ReturnsHtml()
    {
        var markdown = "# Privacy Policy\n\nTest content.";
        File.WriteAllText(Path.Combine(_testDocsDirectory, "privacy-policy.md"), markdown);

        var html = _service.GetDocumentHtml("privacy-policy.md");

        Assert.NotNull(html);
        Assert.Contains("<h1>", html);
        Assert.Contains("Privacy Policy", html);
    }

    [Fact]
    public void GetDocumentHtml_PublicOffer_ReturnsHtml()
    {
        var markdown = "# Public Offer\n\nTest content.";
        File.WriteAllText(Path.Combine(_testDocsDirectory, "public-offer.md"), markdown);

        var html = _service.GetDocumentHtml("public-offer.md");

        Assert.NotNull(html);
        Assert.Contains("<h1>", html);
        Assert.Contains("Public Offer", html);
    }

    [Fact]
    public void GetDocumentHtml_ReplacesAllLegalPlaceholders()
    {
        var markdown = """
            # Публичная оферта {{RWT_SERVICE_NAME}}

            Дата: {{RWT_DOCUMENT_DATE}}
            Домен: {{RWT_PUBLIC_DOMAIN}}
            Оператор: {{RWT_OPERATOR_NAME}}
            Реквизиты: {{RWT_OPERATOR_DETAILS}}
            Поддержка: {{RWT_SUPPORT_CONTACT}}
            """;
        File.WriteAllText(Path.Combine(_testDocsDirectory, "public-offer.md"), markdown);

        var html = _service.GetDocumentHtml("public-offer.md");

        Assert.NotNull(html);
        Assert.DoesNotContain("{{RWT_", html);
        Assert.Contains("Rawrter", html);
        Assert.Contains("26.10.2026", html);
        Assert.Contains("https://rawrter.com", html);
        Assert.Contains("ОГРНИП: 323645700043305", html);
        Assert.Contains("denduka@yandex.ru", html);
    }

    [Fact]
    public void GetDocumentHtml_UnknownDocument_ReturnsNull()
    {
        var html = _service.GetDocumentHtml("unknown.md");
        Assert.Null(html);
    }

    [Fact]
    public void GetDocumentHtml_MissingFile_ReturnsNull()
    {
        var html = _service.GetDocumentHtml("privacy-policy.md");
        Assert.Null(html);
    }

    [Fact]
    public void GetDocumentHtml_RawHtmlInMarkdown_IsNotRendered()
    {
        var markdown = "# Title\n\n<script>alert('xss')</script>";
        File.WriteAllText(Path.Combine(_testDocsDirectory, "privacy-policy.md"), markdown);

        var html = _service.GetDocumentHtml("privacy-policy.md");

        Assert.NotNull(html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void GetDocumentHtml_CachesResult()
    {
        var markdown = "# Title\n\nContent.";
        File.WriteAllText(Path.Combine(_testDocsDirectory, "privacy-policy.md"), markdown);

        var html1 = _service.GetDocumentHtml("privacy-policy.md");
        File.WriteAllText(Path.Combine(_testDocsDirectory, "privacy-policy.md"), "# Updated");
        var html2 = _service.GetDocumentHtml("privacy-policy.md");

        Assert.Equal(html1, html2);
    }

    [Fact]
    public async Task GetDocumentHtml_ConcurrentReads_ReturnSameHtml()
    {
        var markdown = "# Title\n\nContent.";
        File.WriteAllText(Path.Combine(_testDocsDirectory, "privacy-policy.md"), markdown);

        var results = await Task.WhenAll(
            Enumerable.Range(0, 50)
                .Select(_ => Task.Run(() => _service.GetDocumentHtml("privacy-policy.md"))));

        Assert.All(results, html => Assert.Equal(results[0], html));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDocsDirectory))
            Directory.Delete(_testDocsDirectory, true);
    }
}
