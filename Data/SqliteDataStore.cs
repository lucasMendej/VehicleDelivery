using System.Text.Json;
using System.Globalization;
using Afleverlijst.Configuration;
using Afleverlijst.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Afleverlijst.Data;

public sealed class SqliteDataStore
{
    private readonly string databasePath;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);

    public SqliteDataStore(IWebHostEnvironment environment, IOptions<DeliverySourceOptions> options)
    {
        databasePath = Path.IsPathRooted(options.Value.DatabasePath)
            ? options.Value.DatabasePath
            : Path.Combine(environment.ContentRootPath, options.Value.DatabasePath);
    }

    public string DatabasePath => databasePath;

    public async Task SaveSourceRowsAsync(int set, string mode, DeliverySelection selection,
        IReadOnlyList<Dictionary<string, object?>> rows, bool clearOutput, CancellationToken cancellationToken = default)
    {
        EnsureDirectory();
        await using var connection = Open();
        await connection.OpenAsync(cancellationToken);
        var outputName = selection.OutputFileName.EndsWith($".{mode}", StringComparison.OrdinalIgnoreCase)
            ? selection.OutputFileName
            : $"{selection.OutputFileName}.{mode}";
        var table = SourceTable(set, outputName);
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE TABLE IF NOT EXISTS {Quote(table)} (id INTEGER PRIMARY KEY AUTOINCREMENT, selection_name TEXT NOT NULL, payload_json TEXT NOT NULL, loaded_at TEXT NOT NULL);";
        await command.ExecuteNonQueryAsync(cancellationToken);

        if (clearOutput)
        {
            command.CommandText = $"DELETE FROM {Quote(table)};";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var transaction = connection.BeginTransaction();
        foreach (var row in rows)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = $"INSERT INTO {Quote(table)} (selection_name, payload_json, loaded_at) VALUES ($selection, $payload, $loaded);";
            insert.Parameters.AddWithValue("$selection", selection.Name);
            insert.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(row, jsonOptions));
            insert.Parameters.AddWithValue("$loaded", DateTimeOffset.UtcNow.ToString("O"));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ProcessSetAsync(int set, CancellationToken cancellationToken = default)
    {
        EnsureDirectory();
        await using var connection = Open();
        await connection.OpenAsync(cancellationToken);
        await EnsureReportTableAsync(connection, cancellationToken);

        var salespersonTable = await SupportTableWithLegacyFallback(connection, set, "support_salespersons_initial", cancellationToken);
        var locationTable = await SupportTableWithLegacyFallback(connection, set, "support_locations_initial", cancellationToken);
        var brandTable = await SupportTableWithLegacyFallback(connection, set, "support_brands_initial", cancellationToken);
        var salespersonNames = await LoadLookupAsync(connection, salespersonTable, "userid", "name_", cancellationToken);
        var locationNames = await LoadLookupAsync(connection, locationTable, "code", "name_", cancellationToken);
        var brandNames = await LoadLookupAsync(connection, brandTable, "LETTER", "DESC002", cancellationToken);
        var workshop = await LoadWorkshopRowsAsync(connection, set, cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        foreach (var mode in new[] { "initial", "sync" })
        {
            var table = SourceTable(set, $"vehicles_{mode}");
            if (!await TableExistsAsync(connection, table, cancellationToken)) continue;

            var rows = await ReadRowsAsync(connection, table, cancellationToken);
            foreach (var sourceRow in rows)
            {
                var vehicleType = sourceRow.SelectionName.Contains("used", StringComparison.OrdinalIgnoreCase) ? "U" : "N";
                workshop.TryGetValue((vehicleType, Text(sourceRow.Row, "vehicle", "MAGIC")), out var summary);
                await InsertReportRecordAsync(connection, transaction, set, vehicleType, sourceRow.Row, salespersonNames, locationNames, brandNames, summary, cancellationToken);
            }
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetBrandOptionsAsync(int set, CancellationToken cancellationToken = default)
    {
        EnsureDirectory();
        await using var connection = Open();
        await connection.OpenAsync(cancellationToken);
        var table = await SupportTableWithLegacyFallback(connection, set, "support_brands_initial", cancellationToken);
        var brands = await LoadLookupAsync(connection, table, "LETTER", "DESC002", cancellationToken);
        return brands.Values.Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<DeliveryRecord>> GetDeliveryRecordsAsync(int set, bool includeSoldProgress = false, string soldProgress = "", CancellationToken cancellationToken = default)
    {
        EnsureDirectory();
        await using var connection = Open();
        await connection.OpenAsync(cancellationToken);
        await EnsureReportTableAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT r.vsb_number, r.commission_number, r.license_plate, r.brand, r.vehicle_type, r.model, r.salesperson, COALESCE(NULLIF(r.salesperson_name, ''), r.salesperson), r.sales_location, COALESCE(NULLIF(r.sales_location_name, ''), r.sales_location), r.prep_location, COALESCE(NULLIF(r.prep_location_name, ''), r.prep_location), r.customer_name, r.invoice_name, r.expected_arrival, r.actual_arrival, r.delivery_date, r.delivery_status, r.workshop_rules, r.workshop_forwarded, r.workshop_on_order, r.workshop_invoiced, r.workshop_tip, r.workshop_locations, r.workshop_orders, r.order_notes, r.selected, r.polished, r.user_note, r.chassis, r.progress, COALESCE(f.activestate_1, 0), COALESCE(f.activestate_2, 0), COALESCE(f.text, ''), COALESCE((SELECT l.user_name FROM delivery_ready_log l WHERE l._set = r.set_id AND l.vtype = r.vehicle_type AND l.vehicle = r.vsb_number AND l.selected = 1 ORDER BY l.id DESC LIMIT 1), ''), COALESCE((SELECT l.changed_at FROM delivery_ready_log l WHERE l._set = r.set_id AND l.vtype = r.vehicle_type AND l.vehicle = r.vsb_number AND l.selected = 1 ORDER BY l.id DESC LIMIT 1), ''), COALESCE((SELECT l.user_name FROM delivery_polished_log l WHERE l._set = r.set_id AND l.vtype = r.vehicle_type AND l.vehicle = r.vsb_number AND l.selected = 1 ORDER BY l.id DESC LIMIT 1), ''), COALESCE((SELECT l.changed_at FROM delivery_polished_log l WHERE l._set = r.set_id AND l.vtype = r.vehicle_type AND l.vehicle = r.vsb_number AND l.selected = 1 ORDER BY l.id DESC LIMIT 1), '') FROM delivery_records r LEFT JOIN delivery_record_fields f ON f._set = r.set_id AND f.vtype = r.vehicle_type AND f.vehicle = r.vsb_number WHERE r.set_id = $set AND (r.delivery_date >= $today OR ($includeSold = 1 AND r.progress = $soldProgress)) ORDER BY r.delivery_date, r.vsb_number;";
        command.Parameters.AddWithValue("$set", set);
        command.Parameters.AddWithValue("$today", DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$includeSold", includeSoldProgress ? 1 : 0);
        command.Parameters.AddWithValue("$soldProgress", soldProgress);
        var records = new List<DeliveryRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) records.Add(ReadRecord(reader));
        return records;
    }

    public async Task SaveRowStateAsync(int set, IEnumerable<string> selectedIds, IEnumerable<string> polishedIds, IEnumerable<string> visibleIds, IReadOnlyDictionary<string, string>? notes, string userName, CancellationToken cancellationToken = default)
    {
        EnsureDirectory();
        await using var connection = Open();
        await connection.OpenAsync(cancellationToken);
        var visible = visibleIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = selectedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var polished = polishedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in visible)
        {
            var selectedValue = selected.Contains(id) ? 1 : 0;
            var polishedValue = polished.Contains(id) ? 1 : 0;
            var currentValue = 0;
            var currentPolishedValue = 0;
            await using (var current = connection.CreateCommand())
            {
                current.CommandText = "SELECT selected, polished FROM delivery_records WHERE set_id = $set AND vsb_number = $id;";
                current.Parameters.AddWithValue("$set", set);
                current.Parameters.AddWithValue("$id", id);
                await using var currentReader = await current.ExecuteReaderAsync(cancellationToken);
                if (await currentReader.ReadAsync(cancellationToken))
                {
                    currentValue = currentReader.GetInt32(0);
                    currentPolishedValue = currentReader.GetInt32(1);
                }
            }

            if (currentValue != selectedValue)
            {
                await InsertCheckboxAuditAsync(connection, "delivery_ready_log", set, id, selectedValue, userName, cancellationToken);
            }
            if (currentPolishedValue != polishedValue)
                await InsertCheckboxAuditAsync(connection, "delivery_polished_log", set, id, polishedValue, userName, cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE delivery_records SET selected = $selected, polished = $polished, user_note = $note WHERE set_id = $set AND vsb_number = $id;";
            command.Parameters.AddWithValue("$set", set);
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$selected", selectedValue);
            command.Parameters.AddWithValue("$polished", polishedValue);
            command.Parameters.AddWithValue("$note", notes?.GetValueOrDefault(id)?.Trim() ?? string.Empty);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertCheckboxAuditAsync(SqliteConnection connection, string table, int set, string vehicle, int value, string userName, CancellationToken cancellationToken)
    {
        await using var audit = connection.CreateCommand();
        audit.CommandText = $"INSERT INTO {Quote(table)} (_set, vtype, vehicle, selected, user_name, changed_at) SELECT set_id, vehicle_type, vsb_number, $selected, $user, $changed FROM delivery_records WHERE set_id = $set AND vsb_number = $id;";
        audit.Parameters.AddWithValue("$selected", value);
        audit.Parameters.AddWithValue("$user", userName);
        audit.Parameters.AddWithValue("$changed", DateTimeOffset.Now.ToString("O"));
        audit.Parameters.AddWithValue("$set", set);
        audit.Parameters.AddWithValue("$id", vehicle);
        await audit.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureReportTableAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS delivery_records (id INTEGER PRIMARY KEY AUTOINCREMENT, set_id INTEGER NOT NULL, vsb_number TEXT NOT NULL, chassis TEXT NOT NULL DEFAULT '', commission_number TEXT NOT NULL, license_plate TEXT, brand TEXT NOT NULL, vehicle_type TEXT NOT NULL, model TEXT NOT NULL, salesperson TEXT NOT NULL, salesperson_name TEXT NOT NULL DEFAULT '', sales_location TEXT NOT NULL, sales_location_name TEXT NOT NULL DEFAULT '', prep_location TEXT NOT NULL, prep_location_name TEXT NOT NULL DEFAULT '', customer_name TEXT NOT NULL, invoice_name TEXT NOT NULL DEFAULT '', expected_arrival TEXT, actual_arrival TEXT, delivery_date TEXT, delivery_status TEXT NOT NULL, workshop_rules INTEGER NOT NULL DEFAULT 0, workshop_forwarded INTEGER NOT NULL DEFAULT 0, workshop_on_order INTEGER NOT NULL DEFAULT 0, workshop_invoiced INTEGER NOT NULL DEFAULT 0, workshop_tip TEXT NOT NULL DEFAULT '', workshop_locations TEXT NOT NULL DEFAULT '', workshop_orders TEXT NOT NULL DEFAULT '', order_notes TEXT NOT NULL, selected INTEGER NOT NULL DEFAULT 0, polished INTEGER NOT NULL DEFAULT 0, user_note TEXT NOT NULL DEFAULT ''); CREATE TABLE IF NOT EXISTS delivery_record_fields (id INTEGER PRIMARY KEY AUTOINCREMENT, activestate_1 INTEGER NOT NULL DEFAULT 0, activestate_2 INTEGER NOT NULL DEFAULT 0, text TEXT NOT NULL DEFAULT '', _set INTEGER NOT NULL, vtype TEXT NOT NULL, vehicle TEXT NOT NULL, UNIQUE (_set, vtype, vehicle)); CREATE TABLE IF NOT EXISTS delivery_ready_log (id INTEGER PRIMARY KEY AUTOINCREMENT, _set INTEGER NOT NULL, vtype TEXT NOT NULL, vehicle TEXT NOT NULL, selected INTEGER NOT NULL, user_name TEXT NOT NULL, changed_at TEXT NOT NULL); CREATE TABLE IF NOT EXISTS delivery_polished_log (id INTEGER PRIMARY KEY AUTOINCREMENT, _set INTEGER NOT NULL, vtype TEXT NOT NULL, vehicle TEXT NOT NULL, selected INTEGER NOT NULL, user_name TEXT NOT NULL, changed_at TEXT NOT NULL); CREATE INDEX IF NOT EXISTS ix_delivery_records_set ON delivery_records(set_id); CREATE UNIQUE INDEX IF NOT EXISTS ux_delivery_records_set_vehicle_type ON delivery_records(set_id, vsb_number, vehicle_type); CREATE INDEX IF NOT EXISTS ix_delivery_record_fields_lookup ON delivery_record_fields(_set, vtype, vehicle); CREATE INDEX IF NOT EXISTS ix_delivery_ready_log_lookup ON delivery_ready_log(_set, vtype, vehicle, id); CREATE INDEX IF NOT EXISTS ix_delivery_polished_log_lookup ON delivery_polished_log(_set, vtype, vehicle, id);";
        await command.ExecuteNonQueryAsync(cancellationToken);
        await AddColumnIfMissingAsync(connection, "delivery_records", "salesperson_name", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await AddColumnIfMissingAsync(connection, "delivery_records", "sales_location_name", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await AddColumnIfMissingAsync(connection, "delivery_records", "prep_location_name", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await RenameColumnIfPresentAsync(connection, "delivery_records", "customer_internal_name", "invoice_name", cancellationToken);
        await AddColumnIfMissingAsync(connection, "delivery_records", "invoice_name", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await AddColumnIfMissingAsync(connection, "delivery_records", "chassis", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await AddColumnIfMissingAsync(connection, "delivery_records", "progress", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await AddColumnIfMissingAsync(connection, "delivery_records", "workshop_rules", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await AddColumnIfMissingAsync(connection, "delivery_records", "workshop_forwarded", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await AddColumnIfMissingAsync(connection, "delivery_records", "workshop_on_order", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await AddColumnIfMissingAsync(connection, "delivery_records", "workshop_invoiced", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await AddColumnIfMissingAsync(connection, "delivery_records", "polished", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await AddColumnIfMissingAsync(connection, "delivery_records", "workshop_orders", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await AddColumnIfMissingAsync(connection, "delivery_records", "workshop_tip", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await AddColumnIfMissingAsync(connection, "delivery_records", "workshop_locations", "TEXT NOT NULL DEFAULT ''", cancellationToken);
    }

    private async Task InsertReportRecordAsync(SqliteConnection connection, SqliteTransaction transaction, int set, string vehicleType, Dictionary<string, JsonElement> row, IReadOnlyDictionary<string, string> salespersonNames, IReadOnlyDictionary<string, string> locationNames, IReadOnlyDictionary<string, string> brandNames, WorkshopSummary? workshop, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO delivery_records (set_id, vsb_number, chassis, progress, commission_number, license_plate, brand, vehicle_type, model, salesperson, salesperson_name, sales_location, sales_location_name, prep_location, prep_location_name, customer_name, invoice_name, expected_arrival, actual_arrival, delivery_date, delivery_status, workshop_rules, workshop_forwarded, workshop_on_order, workshop_invoiced, workshop_tip, workshop_locations, workshop_orders, order_notes) VALUES ($set, $vehicle, $chassis, $progress, '', $license, $brand, $type, $model, $salesperson, $salespersonName, $sales, $salesName, $prep, $prepName, $customer, $invoiceName, $expected, $actual, $delivery, $status, $rules, $forwarded, $onOrder, $invoiced, $tip, $workshopLocations, $workshopOrders, '') ON CONFLICT(set_id, vsb_number, vehicle_type) DO UPDATE SET chassis = excluded.chassis, progress = excluded.progress, license_plate = excluded.license_plate, brand = excluded.brand, model = excluded.model, salesperson = excluded.salesperson, salesperson_name = excluded.salesperson_name, sales_location = excluded.sales_location, sales_location_name = excluded.sales_location_name, prep_location = excluded.prep_location, prep_location_name = excluded.prep_location_name, customer_name = excluded.customer_name, invoice_name = excluded.invoice_name, expected_arrival = excluded.expected_arrival, actual_arrival = excluded.actual_arrival, delivery_date = excluded.delivery_date, delivery_status = excluded.delivery_status, workshop_rules = excluded.workshop_rules, workshop_forwarded = excluded.workshop_forwarded, workshop_on_order = excluded.workshop_on_order, workshop_invoiced = excluded.workshop_invoiced, workshop_tip = excluded.workshop_tip, workshop_locations = excluded.workshop_locations, workshop_orders = excluded.workshop_orders;";
        Add(command, "$set", set); Add(command, "$vehicle", Text(row, "vehicle")); Add(command, "$chassis", Text(row, "CHASSIS")); Add(command, "$progress", Text(row, "PROGRESS")); Add(command, "$license", Text(row, "REGN"));
        var brandCode = Text(row, "LETTER", "BRAND", "BRAND_CODE", "FRANCHISE", "FRANCHISE_CODE");
        Add(command, "$brand", brandNames.GetValueOrDefault(brandCode, brandCode));
        var salesperson = Text(row, "exec001"); var salesLocation = Text(row, "SELLLOCN"); var prepLocation = Text(row, "BUYLOCN");
        Add(command, "$type", vehicleType); Add(command, "$model", Text(row, "DESC_")); Add(command, "$salesperson", salesperson); Add(command, "$salespersonName", salespersonNames.GetValueOrDefault(salesperson, salesperson));
        Add(command, "$sales", salesLocation); Add(command, "$salesName", locationNames.GetValueOrDefault(salesLocation, salesLocation)); Add(command, "$prep", prepLocation); Add(command, "$prepName", locationNames.GetValueOrDefault(prepLocation, prepLocation)); Add(command, "$customer", Text(row, "NAME_")); Add(command, "$invoiceName", Text(row, "INAME"));
        var expected = Text(row, "RECDATEE", "SALEDATE");
        var actual = Text(row, "RECDATE", "SINVDATE");
        Add(command, "$expected", expected); Add(command, "$actual", actual); Add(command, "$delivery", Text(row, "DELIVERY_DATE", "DELDATEE", "DELDATE"));
        Add(command, "$status", DetermineStatus(actual, expected, workshop));
        Add(command, "$rules", workshop?.Rules ?? 0); Add(command, "$forwarded", workshop?.Forwarded ?? 0); Add(command, "$onOrder", workshop?.OnOrder ?? 0); Add(command, "$invoiced", workshop?.Invoiced ?? 0); Add(command, "$tip", workshop?.Tip ?? string.Empty);
        Add(command, "$workshopLocations", workshop?.LocationsText(locationNames) ?? string.Empty);
        Add(command, "$workshopOrders", workshop?.OrdersText ?? string.Empty);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<IReadOnlyDictionary<string, string>> LoadLookupAsync(SqliteConnection connection, string table, string keyName, string valueName, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!await TableExistsAsync(connection, table, cancellationToken)) return result;
        foreach (var sourceRow in await ReadRowsAsync(connection, table, cancellationToken))
        {
            var key = Text(sourceRow.Row, keyName);
            if (key.Length > 0)
            {
                var value = Text(sourceRow.Row, valueName);
                if (table.Contains("support_locations", StringComparison.OrdinalIgnoreCase))
                    value = LoadCoordinator.CleanLocationName(value);
                result[key] = value;
            }
        }
        return result;
    }

    private static async Task<string> SupportTableWithLegacyFallback(SqliteConnection connection, int set, string currentTable, CancellationToken cancellationToken)
    {
        if (await TableExistsAsync(connection, SourceTable(set, currentTable), cancellationToken))
            return SourceTable(set, currentTable);

        return SourceTable(set, currentTable[..^"_initial".Length]);
    }

    private async Task<Dictionary<(string Type, string Vehicle), WorkshopSummary>> LoadWorkshopRowsAsync(SqliteConnection connection, int set, CancellationToken cancellationToken)
    {
        var result = new Dictionary<(string, string), WorkshopSummary>();
        var processedRows = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mode in new[] { "initial", "sync" })
        {
            var table = SourceTable(set, $"vehicle_specifications_{mode}");
            if (!await TableExistsAsync(connection, table, cancellationToken)) continue;
            foreach (var sourceRow in await ReadRowsAsync(connection, table, cancellationToken))
            {
                var type = sourceRow.SelectionName.Contains("used", StringComparison.OrdinalIgnoreCase) ? "U" : "N";
                var vehicle = Text(sourceRow.Row, "MAGIC", "vehicle");
                if (vehicle.Length == 0) continue;
                var rowIdentity = string.Join("\u001f", type, vehicle,
                    Text(sourceRow.Row, "desc002"), Text(sourceRow.Row, "socomp"),
                    Text(sourceRow.Row, "poshdr"), Text(sourceRow.Row, "orderno", "order_no"));
                if (!processedRows.Add(rowIdentity)) continue;
                var key = (type, vehicle);
                if (!result.TryGetValue(key, out var summary)) summary = new WorkshopSummary();
                summary.Rules++;
                if (Number(sourceRow.Row, "poshdr") != 0) summary.Forwarded++;
                if (Number(sourceRow.Row, "orderno", "order_no") != 0) summary.OnOrder++;
                var description = Text(sourceRow.Row, "desc002");
                var location = Text(sourceRow.Row, "socomp");
                if (location.Length > 0) summary.Locations.Add(location);
                var order = Text(sourceRow.Row, "orderno");
                var costStatus = Text(sourceRow.Row, "coststat");
                if (location.Length > 0 && order.Length > 0 && Number(sourceRow.Row, "orderno", "order_no") != 0)
                    summary.Orders.Add($"{location}-{order}");
                if (costStatus.Equals("R", StringComparison.OrdinalIgnoreCase)) summary.Invoiced++;
                if (description.Length > 0 || location.Length > 0 || order.Length > 0 || costStatus.Length > 0)
                {
                    var completedMarker = costStatus.Equals("R", StringComparison.OrdinalIgnoreCase) ? "✓ " : string.Empty;
                    summary.Tips.Add($"{completedMarker}{description} locatie {location} werkorder {order} - {costStatus}".Trim());
                }
                result[key] = summary;
            }
        }
        return result;
    }

    private async Task<List<SourceRow>> ReadRowsAsync(SqliteConnection connection, string table, CancellationToken cancellationToken)
    {
        var result = new List<SourceRow>();
        if (!await TableExistsAsync(connection, table, cancellationToken)) return result;
        await using var select = connection.CreateCommand();
        select.CommandText = $"SELECT selection_name, payload_json FROM {Quote(table)} ORDER BY id;";
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new SourceRow(reader.GetString(0), JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(reader.GetString(1), jsonOptions) ?? []));
        }
        return result;
    }

    private static async Task AddColumnIfMissingAsync(SqliteConnection connection, string table, string column, string definition, CancellationToken token)
    {
        await using var check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info({Quote(table)});";
        await using var reader = await check.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) if (reader.GetString(1).Equals(column, StringComparison.OrdinalIgnoreCase)) return;
        await reader.DisposeAsync();
        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {Quote(table)} ADD COLUMN {Quote(column)} {definition};";
        await alter.ExecuteNonQueryAsync(token);
    }

    private static async Task RenameColumnIfPresentAsync(SqliteConnection connection, string table, string oldColumn, string newColumn, CancellationToken token)
    {
        await using var check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info({Quote(table)});";
        var hasOld = false;
        var hasNew = false;
        await using (var reader = await check.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token))
            {
                hasOld |= reader.GetString(1).Equals(oldColumn, StringComparison.OrdinalIgnoreCase);
                hasNew |= reader.GetString(1).Equals(newColumn, StringComparison.OrdinalIgnoreCase);
            }
        }

        if (hasOld && !hasNew)
        {
            await using var rename = connection.CreateCommand();
            rename.CommandText = $"ALTER TABLE {Quote(table)} RENAME COLUMN {Quote(oldColumn)} TO {Quote(newColumn)};";
            await rename.ExecuteNonQueryAsync(token);
        }
    }

    private static int Number(Dictionary<string, JsonElement> row, params string[] names)
        => decimal.TryParse(Text(row, names), NumberStyles.Any, CultureInfo.InvariantCulture, out var value) && value != 0 ? 1 : 0;

    private static string DetermineStatus(string actualText, string expectedText, WorkshopSummary? workshop)
    {
        if (workshop?.AllInvoiced == true) return "Gefactureerd";
        if (workshop?.OnOrder > 0) return "Aangemaakt";
        if (workshop?.Forwarded > 0) return "Gepushed";
        if (DateOnly.TryParse(actualText, out _)) return "Binnen";
        if (DateOnly.TryParse(expectedText, out var expected))
        {
            return expected > DateOnly.FromDateTime(DateTime.Today.AddDays(14))
                ? "Verwacht in 2 weken"
                : "Binnen 2 weken";
        }
        return string.Empty;
    }

    private static DeliveryRecord ReadRecord(SqliteDataReader reader) => new()
    {
        VsbNumber = reader.GetString(0), CommissionNumber = reader.GetString(1), LicensePlate = NullString(reader, 2), Brand = reader.GetString(3), VehicleType = reader.GetString(4), Model = reader.GetString(5), Salesperson = reader.GetString(7), SalesLocation = LoadCoordinator.CleanLocationName(reader.GetString(9)), PrepLocation = LoadCoordinator.CleanLocationName(reader.GetString(11)), CustomerName = reader.GetString(12), InvoiceName = reader.GetString(13), ExpectedArrival = Date(reader, 14), ActualArrival = Date(reader, 15), DeliveryDate = Date(reader, 16), DeliveryStatus = reader.GetString(17), WorkshopRules = reader.GetInt32(18), WorkshopForwarded = reader.GetInt32(19), WorkshopOnOrder = reader.GetInt32(20), WorkshopInvoiced = reader.GetInt32(21), WorkshopTip = reader.GetString(22), WorkshopLocations = reader.GetString(23), WorkshopOrders = reader.GetString(24), OrderNotes = reader.GetString(25), Selected = reader.GetInt64(26) == 1, Polished = reader.GetInt64(27) == 1, UserNote = reader.GetString(28), Chassis = reader.GetString(29), Progress = reader.GetString(30), ActiveState1 = reader.GetInt32(31), ActiveState2 = reader.GetInt32(32), ExtraText = reader.GetString(33), ReadyBy = reader.GetString(34), ReadyAt = reader.GetString(35), PolishedBy = reader.GetString(36), PolishedAt = reader.GetString(37)
    };

    private sealed record SourceRow(string SelectionName, Dictionary<string, JsonElement> Row);
    private sealed class WorkshopSummary
    {
        public int Rules { get; set; }
        public int Forwarded { get; set; }
        public int OnOrder { get; set; }
        public int Invoiced { get; set; }
        public bool AllInvoiced => Rules > 0 && Invoiced == Rules;
        public List<string> Tips { get; } = [];
        public HashSet<string> Locations { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Orders { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string Tip => string.Join(Environment.NewLine, Tips.Distinct());
        public string OrdersText => string.Join(", ", Orders.Order(StringComparer.OrdinalIgnoreCase));
        public string LocationsText(IReadOnlyDictionary<string, string> locationNames) =>
            string.Join("|", Locations
                .Select(code => locationNames.GetValueOrDefault(code, code))
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase));
    }

    private static string Text(Dictionary<string, JsonElement> row, params string[] names) => names.Select(name => row.FirstOrDefault(x => x.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value).Where(x => x.ValueKind != JsonValueKind.Undefined && x.ValueKind != JsonValueKind.Null).Select(x => x.ToString()).FirstOrDefault() ?? string.Empty;
    private static void Add(SqliteCommand command, string name, object value) => command.Parameters.AddWithValue(name, value);
    private static string? NullString(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static DateOnly? Date(SqliteDataReader r, int i) => r.IsDBNull(i) || !DateOnly.TryParse(r.GetString(i), out var d) ? null : d;
    private SqliteConnection Open() => new($"Data Source={databasePath};Cache=Shared");
    private void EnsureDirectory() => Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
    private static string SourceTable(int set, string name) => $"source_{set}_{name}".Replace('.', '_');
    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string table, CancellationToken token)
    { await using var c = connection.CreateCommand(); c.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;"; c.Parameters.AddWithValue("$name", table); return Convert.ToInt64(await c.ExecuteScalarAsync(token)) > 0; }
}
