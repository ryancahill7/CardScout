using CardScout.Core.Data;
using CardScout.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CardScout.Web.Pages;

public class RecentListingsModel : PageModel
{
    private readonly RecentListingsRepository _repo;

    public RecentListingsModel(RecentListingsRepository repo)
    {
        _repo = repo;
    }

    public List<RecentListing> Listings { get; set; } = new();

    public async Task OnGetAsync()
    {
        Listings = await _repo.GetRecentAsync();
    }

    public async Task<PartialViewResult> OnGetRowsAsync()
    {
        Listings = await _repo.GetRecentAsync();
        return Partial("_RecentListingsRows", Listings);
    }
}