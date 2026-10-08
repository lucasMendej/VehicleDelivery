using Afleverlijst.Configuration;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Data.Odbc;
using System.Diagnostics;
using System.Text.Json;

namespace Afleverlijst.Data;

public sealed record LoadLogEntry(DateTimeOffset At, int Set, string Level, string Step, string Message);

public sealed class LoadSetStatus
{
    public int Set { get; init; }
    public string SourceName { get; set; } = string.Empty;
    public string Dsn { get; set; } = string.Empty;
    public string State { get; set; } = "not_started";
    public int Version { get; set; }
    public string LastMode { get; set; } = string.Empty;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public DateTimeOffset? LastSupportRefreshAt { get; set; }
    public DateTimeOffset? LastSyncAt { get; set; }
    public string? Error { get; set; }
    public int ActiveSessions { get; set; }
    public List<LoadLogEntry> Logs { get; } = [];
    public bool IsRunning => State is "initial_loading" or "syncing" or "processing";
}

public sealed class LoadCoordinator
{
    private readonly DeliverySourceOptions options;
    private readonly DeliverySelectionCatalog selections;
    private readonly ILogger<LoadCoordinator> logger;
    private readonly IWebHostEnvironment environment;
    private readonly SqliteDataStore dataStore;
    private readonly ConcurrentDictionary<int, LoadSetStatus> statuses = new();
    private readonly ConcurrentDictionary<string, (int Set, DateTimeOffset SeenAt)> sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim[] locks = [new(1, 1), new(1, 1), new(1, 1), new(1, 1)];

    public LoadCoordinator(IOptions<DeliverySourceOptions> options, DeliverySelectionCatalog selections, ILogger<LoadCoordinator> logger, IWebHostEnvironment environment, SqliteDataStore dataStore)
    {
        this.options = options.Value;
        this.selections = selections;
        this.logger = logger;
        this.environment = environment;
        this.dataStore = dataStore;
    }

    public IReadOnlyList<LoadSetStatus> AllStatuses => statuses.Values.OrderBy(x => x.Set).ToList();

    public LoadSetStatus GetStatus(int set)
    {
        var source = options.ForSet(set);
        return statuses.GetOrAdd(set, _ => new LoadSetStatus { Set = set, SourceName = source.Name, Dsn = source.Dsn });
    }

    public string SelectionDebug(int set, string mode) => selections.RenderForDebug(set, mode, options);

    public void TouchSession(int set, string sessionId)
    {
        sessions[sessionId] = (set, DateTimeOffset.UtcNow);
        var status = GetStatus(set);
        status.ActiveSessions = ActiveSessions(set);
        StartSyncIfDue(set);
    }

    public void RemoveSession(int set, string sessionId)
    {
        sessions.TryRemove(sessionId, out _);
        GetStatus(set).ActiveSessions = ActiveSessions(set);
    }

    public int ActiveSessions(int set)
    {
        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-options.SessionTimeoutSeconds);
        foreach (var item in sessions.Where(x => x.Value.SeenAt < cutoff).ToList()) sessions.TryRemove(item.Key, out _);
        return sessions.Count(x => x.Value.Set == set);
    }

    public Task EnsureInitialLoadAsync(int set) => StartAsync(set, "initial", false);
    public Task StartSyncAsync(int set) => StartAsync(set, "sync", true);
    public bool StartSyncIfDue(int set)
    {
        var status = GetStatus(set);
        if (status.ActiveSessions <= 0 || status.LastSyncAt is null || status.IsRunning || status.State == "failed")
            return false;

        if (status.LastSyncAt.Value > DateTimeOffset.Now.AddMinutes(-Math.Max(1, options.SyncIntervalMinutes)))
            return false;

        StartSyncAsync(set);
        return true;
    }
    public Task StartProcessingAsync(int set)
    {
        _ = Task.Run(() => ProcessAsync(set));
        return Task.CompletedTask;
    }

    private Task StartAsync(int set, string mode, bool onlyIfNotRunning)
    {
        var status = GetStatus(set);
        if (onlyIfNotRunning && status.IsRunning) return Task.CompletedTask;
        if (onlyIfNotRunning)
        {
            status.State = "syncing";
            status.LastMode = "sync";
        }
        _ = Task.Run(() => RunAsync(set, mode));
        return Task.CompletedTask;
    }

    public async Task RunAsync(int set, string mode)
    {
        var gate = locks[set];
        if (!await gate.WaitAsync(0)) return;
        var status = GetStatus(set);
        status.State = mode == "initial" ? "initial_loading" : "syncing";
        status.LastMode = mode;
        status.StartedAt = DateTimeOffset.Now;
        status.FinishedAt = null;
        status.Error = null;
        Log(status, "info", "start", $"Start {mode} load voor _set={set}.");
        try
        {
            var source = options.ForSet(set);
            Log(status, "info", "configuration", $"Bron: {source.Name}; DSN: {source.Dsn}; UseSampleData: {options.UseSampleData}.");
            foreach (var selection in selections.ForSet(set, mode))
            {
                var sql = selections.RenderSql(selection, source);
                Log(status, "info", selection.Name, $"Selectie voorbereid: {selection.Purpose}.");
                Log(status, "debug", selection.Name, sql);
            }

            if (options.UseSampleData)
            {
                await Task.Delay(250);
                Log(status, "warning", "sample", "Geen ODBC uitgevoerd: UseSampleData=true. Productiemodus voert de catalogusselecties uit.");
            }
            else
            {
                await ExecuteAndSaveAsync(set, mode, source, status);
                status.State = "processing";
                status.LastMode = $"{mode} process";
                Log(status, "info", "process", $"Lokale verwerking gestart na {mode} load vanuit SQLite.");
                await dataStore.ProcessSetAsync(set);
                Log(status, "info", "process", $"Lokale verwerking afgerond vanuit SQLite ({dataStore.DatabasePath}).");
            }

            if (mode == "initial")
            {
                status.LastSupportRefreshAt = DateTimeOffset.Now;
                status.Version = 0;
            }
            else
            {
                status.Version++;
            }
            status.LastSyncAt = DateTimeOffset.Now;
            status.State = "ready";
            Log(status, "info", "complete", $"{mode} load afgerond.");
        }
        catch (Exception exception)
        {
            status.State = "failed";
            status.Error = exception.Message;
            Log(status, "error", "failed", exception.ToString());
            logger.LogError(exception, "Load failed for set {Set}", set);
        }
        finally
        {
            status.FinishedAt = DateTimeOffset.Now;
            status.ActiveSessions = ActiveSessions(set);
            gate.Release();
        }
    }

    private async Task ExecuteAndSaveAsync(int set, string mode, DeliverySourceDefinition source, LoadSetStatus status)
    {
        var connectionString = BuildConnectionString(source);
        var reader = new KisamSourceReader(connectionString);
        var outputRoot = Path.Combine(environment.ContentRootPath, options.DataPath, set.ToString(), mode);
        Directory.CreateDirectory(outputRoot);

        var combinedRows = new Dictionary<string, List<Dictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase);
        var manifest = new List<object>();
        foreach (var selection in selections.ForSet(set, mode))
        {
            Log(status, "info", selection.Name, "ODBC-selectie starten.");
            var startedAt = Stopwatch.GetTimestamp();
            var sql = selections.RenderSql(selection, source);
            var rows = (await reader.ExecuteAsync(sql)).ToList();
            NormalizeDeliveryDate(rows, selection, source);
            if (selection.Name.Equals("support.locations", StringComparison.OrdinalIgnoreCase))
            {
                NormalizeLocationNames(rows);
                Log(status, "debug", selection.Name, "Locatienamen opgeschoond: Hedin, Automotive, 1M en 3F verwijderd.");
            }
            var elapsed = Stopwatch.GetElapsedTime(startedAt);
            await dataStore.SaveSourceRowsAsync(set, mode, selection, rows, !combinedRows.ContainsKey(selection.OutputFileName));
            if (!combinedRows.TryGetValue(selection.OutputFileName, out var outputRows))
            {
                outputRows = [];
                combinedRows[selection.OutputFileName] = outputRows;
            }

            outputRows.AddRange(rows);
            var fileName = ToSafeFileName(selection.OutputFileName) + ".json";
            manifest.Add(new { selection = selection.Name, file = fileName, rowCount = rows.Count });
            Log(status, "info", selection.Name, $"{rows.Count} rijen opgehaald in {elapsed.TotalSeconds:0.0}s; lokaal samengevoegd in {Path.Combine(options.DataPath, set.ToString(), mode, fileName)}.");
        }

        foreach (var output in combinedRows)
        {
            var fileName = ToSafeFileName(output.Key) + ".json";
            var outputPath = Path.Combine(outputRoot, fileName);
            // Bewust een ruwe kopie: de JSON bevat dezelfde kolomnamen als de ODBC-resultset.
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(output.Value, new JsonSerializerOptions { WriteIndented = true }));
        }

        var manifestPath = Path.Combine(outputRoot, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            set,
            mode,
            source = source.Name,
            dsn = source.Dsn,
            retrievedAt = DateTimeOffset.Now,
            selections = manifest
        }, new JsonSerializerOptions { WriteIndented = true }));
        Log(status, "info", "odbc", $"ODBC-load afgerond; manifest opgeslagen in {Path.Combine(options.DataPath, set.ToString(), mode, "manifest.json")}.");
    }

    private static void NormalizeLocationNames(IEnumerable<Dictionary<string, object?>> rows)
    {
        foreach (var row in rows)
        {
            var nameKey = row.Keys.FirstOrDefault(key => key.Equals("name_", StringComparison.OrdinalIgnoreCase));
            if (nameKey is null || row[nameKey] is not string name) continue;

            row[nameKey] = CleanLocationName(name);
        }
    }

    private static void NormalizeDeliveryDate(IEnumerable<Dictionary<string, object?>> rows, DeliverySelection selection, DeliverySourceDefinition source)
    {
        if (!selection.Name.StartsWith("vehicles.", StringComparison.OrdinalIgnoreCase)) return;

        var sourceField = selection.Name.Contains(".used.", StringComparison.OrdinalIgnoreCase)
            ? source.FieldMapping.UsedDeliveryDate
            : source.FieldMapping.NewDeliveryDate;
        var sourceKey = sourceField;
        foreach (var row in rows)
        {
            var key = row.Keys.FirstOrDefault(x => x.Equals(sourceKey, StringComparison.OrdinalIgnoreCase));
            if (key is not null) row["DELIVERY_DATE"] = row[key];
        }
    }

    internal static string CleanLocationName(string name) => name
        .Replace("Hedin ", string.Empty, StringComparison.OrdinalIgnoreCase)
        .Replace("Automotive ", string.Empty, StringComparison.OrdinalIgnoreCase)
        .Replace("1M ", string.Empty, StringComparison.OrdinalIgnoreCase)
        .Replace("3F ", string.Empty, StringComparison.OrdinalIgnoreCase)
        .Trim();

    private async Task ProcessAsync(int set)
    {
        var gate = locks[set];
        if (!await gate.WaitAsync(0)) return;
        var status = GetStatus(set);
        status.State = "processing";
        status.LastMode = "process";
        status.StartedAt = DateTimeOffset.Now;
        status.FinishedAt = null;
        status.Error = null;
        Log(status, "info", "process", $"Lokale verwerking gestart voor _set={set}.");
        try
        {
            await dataStore.ProcessSetAsync(set);
            status.State = "ready";
            status.LastSyncAt = DateTimeOffset.Now;
            Log(status, "info", "process", $"Lokale verwerking afgerond vanuit SQLite ({dataStore.DatabasePath}).");
        }
        catch (Exception exception)
        {
            status.State = "failed";
            status.Error = exception.Message;
            Log(status, "error", "process", exception.ToString());
            logger.LogError(exception, "Local processing failed for set {Set}", set);
        }
        finally
        {
            status.FinishedAt = DateTimeOffset.Now;
            status.ActiveSessions = ActiveSessions(set);
            gate.Release();
        }
    }

    private static string BuildConnectionString(DeliverySourceDefinition source)
    {
        var builder = new OdbcConnectionStringBuilder
        {
            ["DSN"] = source.Dsn,
            ["UID"] = source.Username,
            ["PWD"] = source.Password
        };
        return builder.ConnectionString;
    }

    private static string ToSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
    }

    private static void Log(LoadSetStatus status, string level, string step, string message)
    {
        lock (status.Logs)
        {
            status.Logs.Add(new LoadLogEntry(DateTimeOffset.Now, status.Set, level, step, message));
            if (status.Logs.Count > 500) status.Logs.RemoveAt(0);
        }
    }
}
