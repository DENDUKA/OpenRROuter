namespace OpenRussiaRouter.WebApp.Pages.Shared;

/// <summary>
/// Data for the copy-paste integration snippets (<c>_IntegrationExamples.cshtml</c>):
/// the API base URL (already including <c>/v1</c>), a concrete model id, and its context window.
/// </summary>
public sealed record IntegrationExamplesModel(string V1BaseUrl, string ModelId, int ContextWindow);
