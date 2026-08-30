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
        Assert.Contains("var inertiaVelocityPxPerMs = 0;", script, StringComparison.Ordinal);
        Assert.Contains("var resumeDelayMs = 800;", script, StringComparison.Ordinal);
        Assert.Contains("var inertiaDecayPerFrame = 0.92;", script, StringComparison.Ordinal);
        Assert.Contains("var maximumInertiaVelocityPxPerMs = 2;", script, StringComparison.Ordinal);
        Assert.Contains("var maximumVelocitySampleAgeMs = 80;", script, StringComparison.Ordinal);
        Assert.Contains("function synchronizeCycleHover()", script, StringComparison.Ordinal);
        Assert.Contains("synchronizeCycleHover();", script, StringComparison.Ordinal);
        Assert.Equal(3, script.Split("synchronizeCycleHover();", StringSplitOptions.None).Length - 1);
        Assert.Contains("var wasHoveredCycle = hoveredCycle;", script, StringComparison.Ordinal);
        Assert.Contains("wasHoveredCycle || !Number.isFinite(resumeAutoplayAt)", script, StringComparison.Ordinal);
        Assert.Contains("data-flagship-cycle", script, StringComparison.Ordinal);
        Assert.Contains("pointerover", script, StringComparison.Ordinal);
        Assert.Contains("pointerout", script, StringComparison.Ordinal);
        Assert.Contains("wheel", script, StringComparison.Ordinal);
        Assert.Contains("pagehide", script, StringComparison.Ordinal);
        Assert.Contains("pageshow", script, StringComparison.Ordinal);
        Assert.Contains("prefers-reduced-motion: reduce", script, StringComparison.Ordinal);
        Assert.Contains("aria-hidden", script, StringComparison.Ordinal);
        Assert.Contains("tabIndex = -1", script, StringComparison.Ordinal);
        Assert.Contains("requestAnimationFrame", script, StringComparison.Ordinal);
    }
}
