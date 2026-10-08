using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.DataProtection;
using Afleverlijst.Data;
using Afleverlijst.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, ".keys")));

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.Configure<FormOptions>(options =>
{
    // Set 3 kan meer zichtbare regels bevatten. Per regel worden de
    // checkbox-, visible-id- en notitievelden gepost.
    options.ValueCountLimit = 10_000;
});
builder.Services.Configure<DeliverySourceOptions>(builder.Configuration.GetSection("DeliverySources"));
builder.Services.AddSingleton<DeliverySelectionCatalog>();
builder.Services.AddSingleton<SqliteDataStore>();
builder.Services.AddSingleton<LoadCoordinator>();
builder.Services.AddHostedService<LoadBackgroundService>();
builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme)
    .AddNegotiate();
builder.Services.AddAuthorization();
builder.Services.AddSingleton<IDeliveryRepository>(services =>
{
    var configured = services.GetRequiredService<IOptions<DeliverySourceOptions>>().Value;
    return configured.UseSampleData
        ? new SampleDeliveryRepository(services.GetRequiredService<IWebHostEnvironment>())
        : new SqliteDeliveryRepository(services.GetRequiredService<SqliteDataStore>());
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/api/load/heartbeat", (HttpRequest request, LoadCoordinator coordinator) =>
{
    var set = ParseSet(request.Query["_set"]);
    var session = request.Query["session"].ToString();
    if (string.IsNullOrWhiteSpace(session)) return Results.BadRequest(new { error = "session is required" });
    coordinator.TouchSession(set, session);
    return Results.Ok(coordinator.GetStatus(set));
});

app.MapPost("/api/load/close", (HttpRequest request, LoadCoordinator coordinator) =>
{
    var set = ParseSet(request.Query["_set"]);
    var session = request.Query["session"].ToString();
    if (!string.IsNullOrWhiteSpace(session)) coordinator.RemoveSession(set, session);
    return Results.Ok();
});

app.MapGet("/api/load/status", (HttpRequest request, LoadCoordinator coordinator) =>
{
    var set = ParseSet(request.Query["_set"]);
    return Results.Ok(coordinator.GetStatus(set));
});

app.MapPost("/api/load/process", (HttpRequest request, LoadCoordinator coordinator) =>
{
    var set = ParseSet(request.Query["_set"]);
    coordinator.StartProcessingAsync(set);
    return Results.Accepted($"/api/load/status?_set={set}");
});

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();

static int ParseSet(string? value) => value is "3" ? 3 : 1;
