namespace Afleverlijst.Configuration;

public sealed class DeliverySourceOptions
{
    public int DefaultSet { get; set; } = 1;
    public int SyncIntervalMinutes { get; set; } = 10;
    public int SessionTimeoutSeconds { get; set; } = 90;
    public bool UseSampleData { get; set; } = true;
    public string DataPath { get; set; } = "data";
    public string DatabasePath { get; set; } = "data/afleverlijst.sqlite";
    public Dictionary<string, DeliverySourceDefinition> Sources { get; set; } = new();

    public DeliverySourceDefinition ForSet(int set) =>
        Sources.TryGetValue(set.ToString(), out var source)
            ? source
            : throw new InvalidOperationException($"Geen ODBC-bron geconfigureerd voor _set={set}.");
}

public sealed class DeliverySourceDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Dsn { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    // Configuration binding is case-insensitive, but does not treat
    // "sold_progress" and "SoldProgress" as the same key.
    public string sold_progress { get; set; } = string.Empty;
    public string SoldProgress { get; set; } = string.Empty;
    public string SoldProgressValue => string.IsNullOrWhiteSpace(sold_progress) ? SoldProgress : sold_progress;
    public DeliveryFieldMapping FieldMapping { get; set; } = new();
}

public sealed class DeliveryFieldMapping
{
    public string NewDeliveryDate { get; set; } = "DELDATEE";
    public string UsedDeliveryDate { get; set; } = "DELDATE";
}
