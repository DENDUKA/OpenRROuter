using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenRussiaRouter.WebApp.Services.Legal;

namespace OpenRussiaRouter.WebApp.Pages.Legal;

public class OfferModel : PageModel
{
    private readonly LegalDocumentService _legalDocumentService;

    public string DocumentHtml { get; private set; } = "";

    public OfferModel(LegalDocumentService legalDocumentService)
    {
        _legalDocumentService = legalDocumentService;
    }

    public void OnGet()
    {
        DocumentHtml = _legalDocumentService.GetDocumentHtml("public-offer.md") ?? "Document not found.";
    }
}