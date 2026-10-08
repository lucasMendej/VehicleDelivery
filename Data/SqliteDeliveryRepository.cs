using Afleverlijst.Models;

namespace Afleverlijst.Data;

public sealed class SqliteDeliveryRepository(SqliteDataStore store) : IDeliveryRepository
{
    public DeliveryListResult Query(int set, DeliveryQuery query)
    {
        var records = store.GetDeliveryRecordsAsync(set, query.PushToWorkshop, query.SoldProgress).GetAwaiter().GetResult();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var currentRecords = records.Where(x => x.DeliveryDate is not null && x.DeliveryDate.Value >= today).ToList();
        if (query.PushToWorkshop)
            currentRecords = records.Where(x => x.Progress.Equals(query.SoldProgress, StringComparison.OrdinalIgnoreCase)).ToList();
        IEnumerable<DeliveryRecord> result = currentRecords;
        if (query.PushToWorkshop)
            result = result.Where(x => x.WorkshopRules > 0 && x.WorkshopForwarded < x.WorkshopRules && x.Chassis.Length > 0);
        result = result.Where(x => Matches(x.DeliveryStatus, query.DeliveryStatus));
        result = result.Where(x => Matches(x.Brand, query.Brand));
        result = result.Where(x => Matches(x.VehicleType, query.VehicleType));
        result = result.Where(x => Matches(x.SalesLocation, query.SalesLocation));
        result = result.Where(x => Matches(x.PrepLocation, query.PrepLocation));
        result = result.Where(x => WorkshopMatches(x.WorkshopLocations, query.WorkshopLocation));
        result = result.Where(x => Contains(x.Salesperson, query.Salesperson));
        result = result.Where(x => ContainsCustomer(x, query.Customer));
        result = query.Sort switch
        {
            "vsb_number" => Order(result, x => x.VsbNumber, query.Descending),
            "customer_name" => Order(result, x => x.CustomerName, query.Descending),
            "salesperson" => Order(result, x => x.Salesperson, query.Descending),
            "delivery_status" => Order(result, x => x.DeliveryStatus, query.Descending),
            "expected_arrival" => Order(result, x => x.ExpectedArrival, query.Descending),
            _ => Order(result, x => x.DeliveryDate ?? DateOnly.MaxValue, query.Descending)
        };
        return new DeliveryListResult
        {
            Records = result.ToList(),
            StatusOptions = currentRecords.Select(x => x.DeliveryStatus).Where(x => x.Length > 0).Distinct().Order().ToList(),
            BrandOptions = currentRecords.Select(x => x.Brand).Where(x => x.Length > 0)
                .Concat(store.GetBrandOptionsAsync(set).GetAwaiter().GetResult())
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList(),
            VehicleTypeOptions = currentRecords.Select(x => x.VehicleType).Where(x => x.Length > 0).Distinct().Order().ToList(),
            SalesLocationOptions = currentRecords.Select(x => x.SalesLocation).Where(x => x.Length > 0).Distinct().Order().ToList(),
            PrepLocationOptions = currentRecords.Select(x => x.PrepLocation).Where(x => x.Length > 0).Distinct().Order().ToList()
            ,WorkshopLocationOptions = currentRecords.SelectMany(x => x.WorkshopLocations.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    public void SaveRowState(int set, IEnumerable<string> selectedIds, IEnumerable<string> polishedIds, IEnumerable<string> visibleIds, IReadOnlyDictionary<string, string>? notes, string userName) =>
        store.SaveRowStateAsync(set, selectedIds, polishedIds, visibleIds, notes, userName).GetAwaiter().GetResult();

    private static bool Matches(string value, string filter) => string.IsNullOrWhiteSpace(filter) || value.Equals(filter, StringComparison.OrdinalIgnoreCase);
    private static bool Contains(string value, string filter) => string.IsNullOrWhiteSpace(filter) || value.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);
    private static bool ContainsCustomer(DeliveryRecord record, string filter) => string.IsNullOrWhiteSpace(filter) || Contains(record.CustomerName, filter) || Contains(record.InvoiceName, filter);
    private static bool WorkshopMatches(string values, string filter) => string.IsNullOrWhiteSpace(filter) || values.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(value => value.Equals(filter, StringComparison.OrdinalIgnoreCase));
    private static IEnumerable<DeliveryRecord> Order<TKey>(IEnumerable<DeliveryRecord> source, Func<DeliveryRecord, TKey> key, bool descending) => descending ? source.OrderByDescending(key) : source.OrderBy(key);
}
