namespace OpenRussiaRouter.WebApp.Pages.Shared;

/// <summary>
/// One configuration method on a tool detail page (e.g. "wizard", "env vars").
/// The actual code text is read from the <c>IViewLocalizer</c> by key in the
/// <c>_CodeBlock</c> partial — see <c>_DocsToolLayout.cshtml</c>.
/// </summary>
public sealed class DocsToolWay
{
    public string TitleKey { get; init; } = "";
    public string DescKey { get; init; } = "";
    /// <summary>Localization key for the code snippet (raw template with <c>{BASE_URL}</c> / <c>{MODEL_ID}</c>).</summary>
    public string CodeKey { get; init; } = "";
}

/// <summary>
/// Model passed to <c>_DocsToolLayout</c>. Each detail page fills this in
/// (per-tool card data, configuration methods, caveats, sidebar list).
/// </summary>
public sealed class DocsToolLayoutModel
{
    public string PageTitle { get; init; } = "";
    public string AccentColor { get; init; } = "#7C3AED";
    public string IconLetter { get; init; } = "";
    public string CategoryKey { get; init; } = "";
    public string NameKey { get; init; } = "";
    public string HeroKey { get; init; } = "";

    public string V1BaseUrl { get; init; } = "";
    public string ModelId { get; init; } = "<paste-a-model-id>";

    public IReadOnlyList<DocsToolWay> Ways { get; init; } = Array.Empty<DocsToolWay>();

    /// <summary>Show the Anthropic-format limitation note (Claude Code only).</summary>
    public bool ShowAnthropicNote { get; init; }

    public IReadOnlyList<string> CaveatKeys { get; init; } = Array.Empty<string>();

    /// <summary>Localization key for the verify-connection code snippet.</summary>
    public string VerifyCodeKey { get; init; } = "Docs_VerifyCode";

    /// <summary>Other tools to list in the "Other tools" sidebar (excludes the current one).</summary>
    public IReadOnlyList<DocsTool> OtherTools { get; init; } = Array.Empty<DocsTool>();
}
