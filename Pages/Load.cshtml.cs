using Afleverlijst.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Afleverlijst.Configuration;
using Microsoft.Extensions.Options;

namespace Afleverlijst.Pages;

public sealed class LoadModel(LoadCoordinator coordinator, IOptions<DeliverySourceOptions> options) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "_set")]
    public int Set { get; set; } = 1;

    public LoadSetStatus Status { get; private set; } = new() { Set = 1 };
    public string InitialSelections { get; private set; } = string.Empty;
    public string SyncSelections { get; private set; } = string.Empty;
    public int SyncIntervalMinutes { get; } = options.Value.SyncIntervalMinutes;

    public void OnGet()
    {
        Set = Set == 3 ? 3 : 1;
        Status = coordinator.GetStatus(Set);
        InitialSelections = coordinator.SelectionDebug(Set, "initial");
        SyncSelections = coordinator.SelectionDebug(Set, "sync");
    }

    public IActionResult OnPostInitial()
    {
        Set = Set == 3 ? 3 : 1;
        coordinator.EnsureInitialLoadAsync(Set);
        return RedirectToPage(new { _set = Set });
    }

    public IActionResult OnPostSync()
    {
        Set = Set == 3 ? 3 : 1;
        coordinator.StartSyncAsync(Set);
        return RedirectToPage(new { _set = Set });
    }

    public IActionResult OnPostProcess()
    {
        Set = Set == 3 ? 3 : 1;
        coordinator.StartProcessingAsync(Set);
        return RedirectToPage(new { _set = Set });
    }
}
