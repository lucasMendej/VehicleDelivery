using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Afleverlijst.Data;
using Afleverlijst.Models;

namespace Afleverlijst.Pages;

public class IndexModel(IDeliveryRepository repository, LoadCoordinator loadCoordinator) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "_set")] public int Set { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public string DeliveryStatus { get; set; } = string.Empty;
    [BindProperty(SupportsGet = true)] public string Brand { get; set; } = string.Empty;
    [BindProperty(SupportsGet = true)] public string VehicleType { get; set; } = string.Empty;
    [BindProperty(SupportsGet = true)] public string SalesLocation { get; set; } = string.Empty;
    [BindProperty(SupportsGet = true)] public string PrepLocation { get; set; } = string.Empty;
    [BindProperty(SupportsGet = true)] public string WorkshopLocation { get; set; } = string.Empty;
    [BindProperty(SupportsGet = true)] public string Salesperson { get; set; } = string.Empty;
    [BindProperty(SupportsGet = true)] public string Customer { get; set; } = string.Empty;
    [BindProperty(SupportsGet = true)] public string Sort { get; set; } = "delivery_date";
    [BindProperty(SupportsGet = true)] public bool Descending { get; set; }
    [BindProperty] public List<string> SelectedIds { get; set; } = [];
    [BindProperty] public List<string> PolishedIds { get; set; } = [];
    [BindProperty] public List<string> VisibleIds { get; set; } = [];
    [BindProperty] public Dictionary<string, string> Notes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DeliveryListResult Result { get; private set; } = new();
    public LoadSetStatus LoadStatus { get; private set; } = new() { Set = 1 };
    public string SessionId { get; private set; } = string.Empty;

    public void OnGet()
    {
        Set = Set == 3 ? 3 : 1;
        SessionId = Request.Cookies["afleverlijst-session"] ?? Guid.NewGuid().ToString("N");
        Response.Cookies.Append("afleverlijst-session", SessionId, new CookieOptions { HttpOnly = false, SameSite = SameSiteMode.Lax, IsEssential = true });
        loadCoordinator.TouchSession(Set, SessionId);
        LoadStatus = loadCoordinator.GetStatus(Set);
        if (LoadStatus.State == "not_started") _ = loadCoordinator.EnsureInitialLoadAsync(Set);
        Result = Query();
    }

    public IActionResult OnPostSave()
    {
        repository.SaveRowState(Set, SelectedIds, PolishedIds, VisibleIds, Notes, User.Identity?.Name ?? "Onbekende gebruiker");
        TempData["SavedMessage"] = $"{SelectedIds.Count} geselecteerde regel(s) opgeslagen.";
        return RedirectToPage(new { _set = Set, DeliveryStatus, Brand, VehicleType, SalesLocation, PrepLocation, WorkshopLocation, Salesperson, Customer, Sort, Descending });
    }

    public DeliveryListResult Query() => repository.Query(Set, new DeliveryQuery
    {
        DeliveryStatus = DeliveryStatus,
        Brand = Brand,
        VehicleType = VehicleType,
        SalesLocation = SalesLocation,
        PrepLocation = PrepLocation,
        WorkshopLocation = WorkshopLocation,
        Salesperson = Salesperson,
        Customer = Customer,
        Sort = Sort,
        Descending = Descending
    });

    public string SortUrl(string field)
    {
        var descending = Sort.Equals(field, StringComparison.OrdinalIgnoreCase) && !Descending;
        return Url.Page("/Index", new { _set = Set, DeliveryStatus, Brand, VehicleType, SalesLocation, PrepLocation, WorkshopLocation, Salesperson, Customer, Sort = field, Descending = descending }) ?? "?";
    }
}
