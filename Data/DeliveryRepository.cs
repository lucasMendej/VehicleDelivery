using System.Data.Odbc;
using System.Text.Json;
using Afleverlijst.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Afleverlijst.Data;

public interface IDeliveryRepository
{
    DeliveryListResult Query(int set, DeliveryQuery query);
    void SaveRowState(int set, IEnumerable<string> selectedIds, IEnumerable<string> polishedIds, IEnumerable<string> visibleIds, IReadOnlyDictionary<string, string>? notes, string userName);
}

public sealed class SampleDeliveryRepository : IDeliveryRepository
{
    private readonly object sync = new();
    private readonly List<DeliveryRecord> records;

    public SampleDeliveryRepository(IWebHostEnvironment environment)
    {
        var path = Path.Combine(environment.ContentRootPath, "dataset", "afleveringen.sample.json");
        var json = File.ReadAllText(path);
        using var document = JsonDocument.Parse(json);
        records = document.RootElement.GetProperty("records")
            .EnumerateArray()
            .Select(Map)
            .ToList();
    }

    public DeliveryListResult Query(int set, DeliveryQuery query)
    {
        lock (sync)
        {
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
                BrandOptions = currentRecords.Select(x => x.Brand).Where(x => x.Length > 0).Distinct().Order().ToList(),
                VehicleTypeOptions = currentRecords.Select(x => x.VehicleType).Where(x => x.Length > 0).Distinct().Order().ToList(),
                SalesLocationOptions = currentRecords.Select(x => x.SalesLocation).Where(x => x.Length > 0).Distinct().Order().ToList(),
                PrepLocationOptions = currentRecords.Select(x => x.PrepLocation).Where(x => x.Length > 0).Distinct().Order().ToList()
            };
        }
    }

    public void SaveRowState(int set, IEnumerable<string> selectedIds, IEnumerable<string> polishedIds, IEnumerable<string> visibleIds, IReadOnlyDictionary<string, string>? notes, string userName)
    {
        var selected = selectedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var polished = polishedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visible = visibleIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (sync)
        {
            foreach (var record in records)
            {
                if (!visible.Contains(record.VsbNumber)) continue;
                record.Selected = selected.Contains(record.VsbNumber);
                record.Polished = polished.Contains(record.VsbNumber);
                record.UserNote = notes?.GetValueOrDefault(record.VsbNumber)?.Trim() ?? string.Empty;
                if (record.Selected)
                {
                    record.ReadyBy = userName;
                    record.ReadyAt = DateTimeOffset.Now.ToString("dd-MM-yyyy HH:mm:ss");
                }
                if (record.Polished)
                {
                    record.PolishedBy = userName;
                    record.PolishedAt = DateTimeOffset.Now.ToString("dd-MM-yyyy HH:mm:ss");
                }
            }
        }
    }

    private static DeliveryRecord Map(JsonElement item) => new()
    {
        VsbNumber = item.GetProperty("vsb_number").GetString() ?? string.Empty,
        Chassis = OptionalString(item, "chassis"),
        Progress = OptionalString(item, "progress"),
        CommissionNumber = item.GetProperty("commission_number").GetString() ?? string.Empty,
        LicensePlate = NullableString(item, "license_plate"),
        Brand = item.GetProperty("brand").GetString() ?? string.Empty,
        VehicleType = item.GetProperty("vehicle_type").GetString() ?? string.Empty,
        Model = item.GetProperty("model").GetString() ?? string.Empty,
        Salesperson = item.GetProperty("salesperson").GetString() ?? string.Empty,
        SalesLocation = item.GetProperty("sales_location").GetString() ?? string.Empty,
        PrepLocation = item.GetProperty("prep_location").GetString() ?? string.Empty,
        CustomerName = item.GetProperty("customer_name").GetString() ?? string.Empty,
        InvoiceName = OptionalString(item, "invoice_name"),
        ExpectedArrival = Date(item, "expected_arrival"),
        ActualArrival = Date(item, "actual_arrival"),
        TransportFrom = Date(item, "transport_from"),
        TransportUntil = Date(item, "transport_until"),
        WorkshopReadyAt = Date(item, "workshop_ready_at"),
        DeliveryDate = Date(item, "delivery_date"),
        DeliveryStatus = item.GetProperty("delivery_status").GetString() ?? string.Empty,
        WorkshopRules = item.GetProperty("workshop_rules").GetInt32(),
        WorkshopForwarded = item.GetProperty("workshop_forwarded").GetInt32(),
        WorkshopOnOrder = item.GetProperty("workshop_on_order").GetInt32(),
        WorkshopInvoiced = item.TryGetProperty("workshop_invoiced", out var invoiced) ? invoiced.GetInt32() : 0,
        OrderNotes = item.GetProperty("order_notes").GetString() ?? string.Empty
    };

    private static string? NullableString(JsonElement item, string name) =>
        item.GetProperty(name).ValueKind == JsonValueKind.Null ? null : item.GetProperty(name).GetString();

    private static string OptionalString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static DateOnly? Date(JsonElement item, string name) =>
        NullableString(item, name) is { } value && DateOnly.TryParse(value, out var date) ? date : null;

    private static bool Matches(string value, string filter) => string.IsNullOrWhiteSpace(filter) || value.Equals(filter, StringComparison.OrdinalIgnoreCase);
    private static bool Contains(string value, string filter) => string.IsNullOrWhiteSpace(filter) || value.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);
    private static bool ContainsCustomer(DeliveryRecord record, string filter) => string.IsNullOrWhiteSpace(filter) || Contains(record.CustomerName, filter) || Contains(record.InvoiceName, filter);

    private static IEnumerable<DeliveryRecord> Order<TKey>(IEnumerable<DeliveryRecord> source, Func<DeliveryRecord, TKey> key, bool descending) =>
        descending ? source.OrderByDescending(key) : source.OrderBy(key);
}

// Production seam: the sample UI can later switch to this repository after the local SQL schema exists.
public sealed class SqlServerDeliveryRepository(SqlConnection connection)
{
    public Task<IEnumerable<DeliveryRecord>> QueryAsync(string sql, object parameters) => connection.QueryAsync<DeliveryRecord>(sql, parameters);
}

// Production seam for the deliberately simple KISAM/Autoline probe.
public sealed class KisamSourceReader(string connectionString)
{
    public async Task<IReadOnlyList<Dictionary<string, object?>>> ExecuteAsync(
        string sql,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new OdbcConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new OdbcCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var column = 0; column < reader.FieldCount; column++)
            {
                var value = reader.GetValue(column);
                row[reader.GetName(column)] = value is DBNull ? null : Normalize(value);
            }

            rows.Add(row);
        }

        return rows;
    }

    private static object Normalize(object value) => value switch
    {
        DateTime dateTime => dateTime.ToString("O"),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O"),
        TimeSpan timeSpan => timeSpan.ToString(),
        byte[] bytes => Convert.ToBase64String(bytes),
        _ => value
    };
}
