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
    public bool ShowAll { get; set; }

    public async Task OnGetAsync(bool showAll = false)
    {
        ShowAll = showAll;
        Deals = await _dealsRepo.GetDealsDetailAsync(onlyProfitable: !showAll);
    }
    public List<DealDetail> Deals { get; set; } = new();


    public async Task<PartialViewResult> OnGetRowsAsync(bool showAll = false)
    {
        Deals = await _dealsRepo.GetDealsDetailAsync(onlyProfitable: !showAll);
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

    public class UpdateStatusRequest
    {
        public string ItemId { get; set; } = "";
        public string UserStatus { get; set; } = "";
    }

    public async Task<IActionResult> OnPostUpdateStatusAsync([FromBody] UpdateStatusRequest request)
    {
        await _dealsRepo.UpdateUserStatusAsync(request.ItemId, request.UserStatus);
        return new JsonResult(new { success = true });
    }
}