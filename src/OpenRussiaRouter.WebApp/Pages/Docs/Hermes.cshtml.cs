using OpenRussiaRouter.WebApp.Pages.Shared;
using OpenRussiaRouter.WebApp.Services;

namespace OpenRussiaRouter.WebApp.Pages.Docs;

public class HermesModel(ApiBaseUrlResolver baseUrlResolver) : DocsToolPageModel(baseUrlResolver)
{
    protected override DocsToolLayoutModel BuildLayout() => new()
    {
        PageTitle = "Hermes Agent — OpenRussiaRouter",
        AccentColor = "#7C3AED",
        IconLetter = "H",
        CategoryKey = "Docs_Cat_CLI",
        NameKey = "Docs_Hermes_Name",
        HeroKey = "Docs_Hermes_Hero",
        V1BaseUrl = V1BaseUrl,
        Ways = new[]
        {
            new DocsToolWay { TitleKey = "Docs_Hermes_Way1Title", DescKey = "Docs_Hermes_Way1Desc", CodeKey = "Docs_Hermes_Way1Code" },
            new DocsToolWay { TitleKey = "Docs_Hermes_Way2Title", DescKey = "Docs_Hermes_Way2Desc", CodeKey = "Docs_Hermes_Way2Code" },
            new DocsToolWay { TitleKey = "Docs_Hermes_Way3Title", DescKey = "Docs_Hermes_Way3Desc", CodeKey = "Docs_Hermes_Way3Code" },
        },
        ShowAnthropicNote = false,
        CaveatKeys = new[] { "Docs_Hermes_Caveat1", "Docs_Hermes_Caveat2" },
        OtherTools = OtherToolsFor("hermes"),
    };
}
