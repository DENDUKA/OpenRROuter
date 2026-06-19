using OpenRussiaRouter.WebApp.Pages.Shared;
using OpenRussiaRouter.WebApp.Services;

namespace OpenRussiaRouter.WebApp.Pages.Docs;

public class OpenClawModel(ApiBaseUrlResolver baseUrlResolver) : DocsToolPageModel(baseUrlResolver)
{
    protected override DocsToolLayoutModel BuildLayout() => new()
    {
        PageTitle = "OpenClaw — OpenRussiaRouter",
        AccentColor = "#0EA5E9",
        IconLetter = "O",
        CategoryKey = "Docs_Cat_Messenger",
        NameKey = "Docs_OpenClaw_Name",
        HeroKey = "Docs_OpenClaw_Hero",
        V1BaseUrl = V1BaseUrl,
        Ways = new[]
        {
            new DocsToolWay { TitleKey = "Docs_OpenClaw_Way1Title", DescKey = "Docs_OpenClaw_Way1Desc", CodeKey = "Docs_OpenClaw_Way1Code" },
        },
        ShowAnthropicNote = false,
        CaveatKeys = new[] { "Docs_OpenClaw_Caveat1", "Docs_OpenClaw_Caveat2" },
        OtherTools = OtherToolsFor("openclaw"),
    };
}
