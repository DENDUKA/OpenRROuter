using OpenRussiaRouter.WebApp.Pages.Shared;
using OpenRussiaRouter.WebApp.Services;

namespace OpenRussiaRouter.WebApp.Pages.Docs;

public class ClaudeCodeModel(ApiBaseUrlResolver baseUrlResolver) : DocsToolPageModel(baseUrlResolver)
{
    protected override DocsToolLayoutModel BuildLayout() => new()
    {
        PageTitle = "Claude Code — OpenRussiaRouter",
        AccentColor = "#D97706",
        IconLetter = "C",
        CategoryKey = "Docs_Cat_CLI",
        NameKey = "Docs_ClaudeCode_Name",
        HeroKey = "Docs_ClaudeCode_Hero",
        V1BaseUrl = V1BaseUrl,
        Ways = new[]
        {
            new DocsToolWay { TitleKey = "Docs_ClaudeCode_Way1Title", DescKey = "Docs_ClaudeCode_Way1Desc", CodeKey = "Docs_ClaudeCode_Way1Code" },
            new DocsToolWay { TitleKey = "Docs_ClaudeCode_Way2Title", DescKey = "Docs_ClaudeCode_Way2Desc", CodeKey = "Docs_ClaudeCode_Way2Code" },
        },
        ShowAnthropicNote = true,
        CaveatKeys = new[] { "Docs_ClaudeCode_Caveat1", "Docs_ClaudeCode_Caveat2" },
        OtherTools = OtherToolsFor("claude-code"),
    };
}
