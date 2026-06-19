using OpenRussiaRouter.WebApp.Pages.Shared;
using OpenRussiaRouter.WebApp.Services;

namespace OpenRussiaRouter.WebApp.Pages.Docs;

public class KiloCodeModel(ApiBaseUrlResolver baseUrlResolver) : DocsToolPageModel(baseUrlResolver)
{
    protected override DocsToolLayoutModel BuildLayout() => new()
    {
        PageTitle = "Kilo Code — OpenRussiaRouter",
        AccentColor = "#10B981",
        IconLetter = "K",
        CategoryKey = "Docs_Cat_IDE",
        NameKey = "Docs_KiloCode_Name",
        HeroKey = "Docs_KiloCode_Hero",
        V1BaseUrl = V1BaseUrl,
        Ways = new[]
        {
            new DocsToolWay { TitleKey = "Docs_KiloCode_Way1Title", DescKey = "Docs_KiloCode_Way1Desc", CodeKey = "Docs_KiloCode_Way1Code" },
        },
        ShowAnthropicNote = false,
        CaveatKeys = new[] { "Docs_KiloCode_Caveat1", "Docs_KiloCode_Caveat2" },
        OtherTools = OtherToolsFor("kilo-code"),
    };
}
