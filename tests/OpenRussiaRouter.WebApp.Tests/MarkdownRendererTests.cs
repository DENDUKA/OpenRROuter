using FluentAssertions;
using OpenRussiaRouter.WebApp.Services.Markup;

namespace OpenRussiaRouter.WebApp.Tests;

public class MarkdownRendererTests
{
    [Fact]
    public void RenderStrict_KeepsImagesHostedByUs()
    {
        var html = MarkdownRenderer.RenderStrict("![screenshot](/tools/image/8f3a1c0e-0000-0000-0000-000000000001)");

        html.Should().Contain("<img");
        html.Should().Contain("/tools/image/8f3a1c0e-0000-0000-0000-000000000001");
    }

    [Fact]
    public void RenderStrict_DropsRemoteImages()
    {
        // A remote image would leak every visitor's IP and user agent to a third party.
        var html = MarkdownRenderer.RenderStrict("![tracker](https://evil.example/pixel.png)");

        html.Should().NotContain("<img");
        html.Should().NotContain("evil.example");
    }

    [Fact]
    public void RenderStrict_DropsProtocolRelativeAndRootRelativeImagesOutsidePrefix()
    {
        MarkdownRenderer.RenderStrict("![x](//evil.example/p.png)").Should().NotContain("<img");
        MarkdownRenderer.RenderStrict("![x](/uploads/p.png)").Should().NotContain("<img");
    }

    [Fact]
    public void RenderStrict_EscapesRawHtml()
    {
        var html = MarkdownRenderer.RenderStrict("<script>alert(1)</script>\n\nhello");

        html.Should().NotContain("<script");
        html.Should().Contain("hello");
    }

    [Fact]
    public void RenderStrict_EscapesRawImgTagWithEventHandler()
    {
        var html = MarkdownRenderer.RenderStrict("""<img src="/tools/image/x" onerror="alert(1)">""");

        html.Should().NotContain("onerror");
    }

    [Fact]
    public void RenderStrict_RendersOrdinaryMarkdown()
    {
        var html = MarkdownRenderer.RenderStrict("## Setup\n\n1. Open settings\n2. Paste the key");

        html.Should().Contain("<h2");
        html.Should().Contain("<ol");
        html.Should().Contain("Paste the key");
    }

    [Fact]
    public void RenderStrict_EmptyInput_ReturnsEmpty()
    {
        MarkdownRenderer.RenderStrict(null).Should().BeEmpty();
        MarkdownRenderer.RenderStrict("   ").Should().BeEmpty();
    }

    [Fact]
    public void Render_StillAllowsRemoteImages_ForModelDescriptions()
    {
        // The lenient renderer is used for OpenRouter-sourced model descriptions, which may
        // legitimately reference remote images. Changing that is out of scope here.
        var html = MarkdownRenderer.Render("![logo](https://cdn.example/logo.png)");

        html.Should().Contain("<img");
        html.Should().Contain("cdn.example");
    }

    [Fact]
    public void Render_StillStripsScripts()
    {
        MarkdownRenderer.Render("<script>alert(1)</script>").Should().NotContain("<script");
    }
}
