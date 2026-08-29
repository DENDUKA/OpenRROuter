namespace OpenRussiaRouter.WebApp.Tests;

public sealed class FlagshipCarouselScriptTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    [Fact]
    public async Task FlagshipCarouselScript_ReturnsBehaviorContractAsync()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/js/flagship-carousel.js");
        var script = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("var speedPxPerSecond = 20;", script, StringComparison.Ordinal);
        Assert.Contains("var dragThresholdPx = 6;", script, StringComparison.Ordinal);
        Assert.Contains("pointerenter", script, StringComparison.Ordinal);
        Assert.Contains("pointerleave", script, StringComparison.Ordinal);
        Assert.Contains("pagehide", script, StringComparison.Ordinal);
        Assert.Contains("pageshow", script, StringComparison.Ordinal);
        Assert.Contains("prefers-reduced-motion: reduce", script, StringComparison.Ordinal);
        Assert.Contains("aria-hidden", script, StringComparison.Ordinal);
        Assert.Contains("tabIndex = -1", script, StringComparison.Ordinal);
        Assert.Contains("requestAnimationFrame", script, StringComparison.Ordinal);
    }
}
