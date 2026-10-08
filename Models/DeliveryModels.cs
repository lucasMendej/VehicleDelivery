namespace Afleverlijst.Models;

public sealed class DeliveryRecord
{
    public string VsbNumber { get; init; } = string.Empty;
    public string Chassis { get; init; } = string.Empty;
    public string Progress { get; init; } = string.Empty;
    public string CommissionNumber { get; init; } = string.Empty;
    public string? LicensePlate { get; init; }
    public string Brand { get; init; } = string.Empty;
    public string VehicleType { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string Salesperson { get; init; } = string.Empty;
    public string SalesLocation { get; init; } = string.Empty;
    public string PrepLocation { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public string InvoiceName { get; init; } = string.Empty;
    public DateOnly? ExpectedArrival { get; init; }
    public DateOnly? ActualArrival { get; init; }
    public DateOnly? TransportFrom { get; init; }
    public DateOnly? TransportUntil { get; init; }
    public DateOnly? WorkshopReadyAt { get; init; }
    public DateOnly? DeliveryDate { get; init; }
    public string DeliveryStatus { get; init; } = string.Empty;
    public int WorkshopRules { get; init; }
    public int WorkshopForwarded { get; init; }
    public int WorkshopOnOrder { get; init; }
    public int WorkshopInvoiced { get; init; }
    public string WorkshopTip { get; init; } = string.Empty;
    public string WorkshopLocations { get; init; } = string.Empty;
    public string WorkshopOrders { get; init; } = string.Empty;
    public string WorkshopProgress => $"{WorkshopRules}-{WorkshopForwarded}-{WorkshopOnOrder}";
    public string OrderNotes { get; init; } = string.Empty;
    public int ActiveState1 { get; init; }
    public int ActiveState2 { get; init; }
    public string ExtraText { get; init; } = string.Empty;
    public string ReadyBy { get; set; } = string.Empty;
    public string ReadyAt { get; set; } = string.Empty;
    public string ReadyTooltip
    {
        get
        {
            if (ReadyAt.Length == 0) return "Nog niet gereed gemeld";

            var separator = ReadyBy.LastIndexOf('\\');
            var userName = separator >= 0 ? ReadyBy[(separator + 1)..] : ReadyBy;
            var dateTime = DateTimeOffset.TryParse(ReadyAt, out var parsed)
                ? parsed.ToString("dd-MM'T'HH:mm")
                : ReadyAt;
            return $"{userName} {dateTime}";
        }
    }
    public bool Selected { get; set; }
    public string PolishedBy { get; set; } = string.Empty;
    public string PolishedAt { get; set; } = string.Empty;
    public bool Polished { get; set; }
    public string PolishedTooltip
    {
        get
        {
            if (PolishedAt.Length == 0) return "Nog niet gepoetst gemeld";
            var separator = PolishedBy.LastIndexOf('\\');
            var userName = separator >= 0 ? PolishedBy[(separator + 1)..] : PolishedBy;
            var dateTime = DateTimeOffset.TryParse(PolishedAt, out var parsed)
                ? parsed.ToString("dd-MM'T'HH:mm")
                : PolishedAt;
            return $"{userName} {dateTime}";
        }
    }
    public string UserNote { get; set; } = string.Empty;
}

public sealed class DeliveryQuery
{
    public bool PushToWorkshop { get; init; }
    public string SoldProgress { get; init; } = string.Empty;
    public string DeliveryStatus { get; init; } = string.Empty;
    public string Brand { get; init; } = string.Empty;
    public string VehicleType { get; init; } = string.Empty;
    public string SalesLocation { get; init; } = string.Empty;
    public string PrepLocation { get; init; } = string.Empty;
    public string WorkshopLocation { get; init; } = string.Empty;
    public string Salesperson { get; init; } = string.Empty;
    public string Customer { get; init; } = string.Empty;
    public string Sort { get; init; } = "delivery_date";
    public bool Descending { get; init; }
}

public sealed class DeliveryListResult
{
    public IReadOnlyList<DeliveryRecord> Records { get; init; } = [];
    public IReadOnlyList<string> StatusOptions { get; init; } = [];
    public IReadOnlyList<string> BrandOptions { get; init; } = [];
    public IReadOnlyList<string> VehicleTypeOptions { get; init; } = [];
    public IReadOnlyList<string> SalesLocationOptions { get; init; } = [];
    public IReadOnlyList<string> PrepLocationOptions { get; init; } = [];
    public IReadOnlyList<string> WorkshopLocationOptions { get; init; } = [];
}
