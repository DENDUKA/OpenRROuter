using OpenRussiaRouter.WebApp.Pages;

namespace OpenRussiaRouter.WebApp.Pages.Docs;

/// <summary>
/// Single source of truth for the catalog rendered on <c>/docs</c>
/// and used to populate the "Other tools" sidebar on detail pages.
/// </summary>
public static class DocsCatalog
{
    public static readonly IReadOnlyList<DocsTool> All = new[]
    {
        new DocsTool(
            Slug: "hermes",
            IconLetter: "H",
            AccentColor: "#7C3AED",
            CategoryKey: "Docs_Cat_CLI",
            NameKey: "Docs_Hermes_Name",
            ShortKey: "Docs_Hermes_Short",
            FeatureKeys: new[] { "Docs_Hermes_F1", "Docs_Hermes_F2", "Docs_Hermes_F3" }),
        new DocsTool(
            Slug: "openclaw",
            IconLetter: "O",
            AccentColor: "#0EA5E9",
            CategoryKey: "Docs_Cat_Messenger",
            NameKey: "Docs_OpenClaw_Name",
            ShortKey: "Docs_OpenClaw_Short",
            FeatureKeys: new[] { "Docs_OpenClaw_F1", "Docs_OpenClaw_F2", "Docs_OpenClaw_F3" }),
        new DocsTool(
            Slug: "kilo-code",
            IconLetter: "K",
            AccentColor: "#10B981",
            CategoryKey: "Docs_Cat_IDE",
            NameKey: "Docs_KiloCode_Name",
            ShortKey: "Docs_KiloCode_Short",
            FeatureKeys: new[] { "Docs_KiloCode_F1", "Docs_KiloCode_F2", "Docs_KiloCode_F3" }),
        new DocsTool(
            Slug: "claude-code",
            IconLetter: "C",
            AccentColor: "#D97706",
            CategoryKey: "Docs_Cat_CLI",
            NameKey: "Docs_ClaudeCode_Name",
            ShortKey: "Docs_ClaudeCode_Short",
            FeatureKeys: new[] { "Docs_ClaudeCode_F1", "Docs_ClaudeCode_F2", "Docs_ClaudeCode_F3" }),
    };

    public static DocsTool? BySlug(string slug) => All.FirstOrDefault(t => t.Slug == slug);
}
