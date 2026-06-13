using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenRussiaRouter.WebApp.Services.Legal;

namespace OpenRussiaRouter.WebApp.Pages.Legal;

public class PrivacyModel : PageModel
{
    private readonly LegalDocumentService _legalDocumentService;

    public string DocumentHtml { get; private set; } = "";

    public PrivacyModel(LegalDocumentService legalDocumentService)
    {
        _legalDocumentService = legalDocumentService;
    }

    public void OnGet()
    {
        DocumentHtml = _legalDocumentService.GetDocumentHtml("privacy-policy.md") ?? "Document not found.";
    }
}