using CardScout.Core.Data;
using CardScout.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CardScout.Web.Pages;

public class DealsModel : PageModel
{
    private readonly DealsRepository _dealsRepo;

    public DealsModel(DealsRepository dealsRepo)
    {
        _dealsRepo = dealsRepo;
    }

    public List<DealDetail> Deals { get; set; } = new();

    public async Task OnGetAsync()
    {
        Deals = await _dealsRepo.GetDealsDetailAsync();
    }

    // Returns just the rows partial, called by JS on a timer
    public async Task<PartialViewResult> OnGetRowsAsync()
    {
        Deals = await _dealsRepo.GetDealsDetailAsync();
        return Partial("_DealsRows", Deals);
    }

    public class UpdateConfidenceRequest
    {
        public string ItemId { get; set; } = "";
        public decimal MatchConfidence { get; set; }
    }

    public async Task<IActionResult> OnPostUpdateConfidenceAsync([FromBody] UpdateConfidenceRequest request)
    {
        await _dealsRepo.UpdateConfidenceAsync(request.ItemId, request.MatchConfidence);
        return new JsonResult(new { success = true });
    }
}