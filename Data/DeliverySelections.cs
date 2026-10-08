using System.Text.Json;
using Afleverlijst.Configuration;

namespace Afleverlijst.Data;

public sealed record DeliverySelection(string Name, string Purpose, string Sql, string? OutputName = null)
{
    public string OutputFileName => OutputName ?? Name;
}

public sealed class DeliverySelectionCatalog
{
    private readonly string path;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);

    public DeliverySelectionCatalog(IWebHostEnvironment environment)
    {
        path = Path.Combine(environment.ContentRootPath, "configuration", "delivery-selections.json");
    }

    public IReadOnlyList<DeliverySelection> Initial => Load().Initial;

    public IReadOnlyList<DeliverySelection> Sync => Load().Sync;

    public IReadOnlyList<DeliverySelection> ForMode(string mode) =>
        string.Equals(mode, "sync", StringComparison.OrdinalIgnoreCase) ? Sync : Initial;

    // De selectiecatalogus is bewust niet set-specifiek. Alleen de DSN en de
    // lokale opslag worden per _set gewisseld; set 1 en set 3 hebben dezelfde
    // querydefinities en verschillen inhoudelijk alleen in hun brondata.
    public IReadOnlyList<DeliverySelection> ForSet(int set, string mode)
    {
        if (set is not (1 or 3))
            throw new ArgumentOutOfRangeException(nameof(set), set, "Alleen _set=1 en _set=3 zijn toegestaan.");

        return ForMode(mode);
    }

    public string RenderSql(DeliverySelection selection, DeliverySourceDefinition source)
    {
        // Sync-selecties hebben een andere naam dan initial-selecties. Bepaal
        // daarom het voertuigtype niet op een specifieke selectienaam, maar
        // op de selectie zelf. Zo blijft used ook bij sync gekoppeld aan
        // UsedDeliveryDate (bijvoorbeeld DELDATE) en wordt niet per ongeluk
        // de new-kolom (bijvoorbeeld DELDATEE) gebruikt.
        var isUsed = selection.Name.Contains("used", StringComparison.OrdinalIgnoreCase)
                     || selection.Sql.Contains("vs_00_usedvehiclestock", StringComparison.OrdinalIgnoreCase);
        var field = isUsed
            ? source.FieldMapping.UsedDeliveryDate
            : source.FieldMapping.NewDeliveryDate;

        if (!IsAllowedDeliveryDateField(field))
            throw new InvalidOperationException($"Ongeldige afleverdatumkolom '{field}' voor bron '{source.Name}'.");

        if (string.IsNullOrWhiteSpace(source.SoldProgressValue))
            throw new InvalidOperationException($"Geen SoldProgress geconfigureerd voor bron '{source.Name}'.");

        var soldProgress = source.SoldProgressValue.Replace("'", "''", StringComparison.Ordinal);
        return selection.Sql
            .Replace("{DELIVERY_DATE}", field, StringComparison.OrdinalIgnoreCase)
            .Replace("{SOLD_PROGRESS}", soldProgress, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAllowedDeliveryDateField(string field) =>
        field.Equals("DELDATE", StringComparison.OrdinalIgnoreCase)
        || field.Equals("DELDATEE", StringComparison.OrdinalIgnoreCase);

    public string RenderForDebug(int set, string mode, DeliverySourceOptions options)
    {
        var source = options.ForSet(set);
        var lines = new List<string>
        {
            $"_set={set}",
            $"source={source.Name}",
            $"dsn={source.Dsn}",
            $"mode={mode}",
            $"selectionFile={path}",
            ""
        };
        lines.AddRange(ForSet(set, mode).Select(x => $"[{x.Name}] {x.Purpose}\n{RenderSql(x, source)}\noutput: {x.OutputFileName}.json\nexecution: vaste query zonder ODBC-parameters\n"));
        return string.Join(Environment.NewLine, lines);
    }

    private SelectionFile Load()
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Selectieconfiguratie niet gevonden: {path}", path);
        }

        return JsonSerializer.Deserialize<SelectionFile>(File.ReadAllText(path), jsonOptions)
            ?? throw new InvalidOperationException($"Selectieconfiguratie is leeg: {path}");
    }

    private sealed class SelectionFile
    {
        public List<DeliverySelection> Initial { get; set; } = [];
        public List<DeliverySelection> Sync { get; set; } = [];
    }
}
