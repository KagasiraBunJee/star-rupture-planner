using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Mvc;
using ModelContextProtocol.AspNetCore;
using StarRupturePlanner.Api;

const string OpenMcpCorsPolicy = "OpenMcpCors";

ApiSettings settings = ApiSettings.FromArgs(args);
Directory.CreateDirectory(settings.DataDir);
Directory.CreateDirectory(settings.ItemAssetDir);
Directory.CreateDirectory(settings.BuildingAssetDir);

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton<LocalizationStore>();
builder.Services.AddSingleton<ResourceService>();
builder.Services.AddCors(options =>
{
    options.AddPolicy(OpenMcpCorsPolicy, policy =>
    {
        policy
            .AllowAnyHeader()
            .AllowAnyMethod()
            .SetIsOriginAllowed(_ => true);
    });
});
builder.Services.AddMcpServer()
    .WithHttpTransport(options =>
    {
        options.Stateless = false;
#pragma warning disable MCP9004
        options.EnableLegacySse = true;
#pragma warning restore MCP9004
    })
    .WithToolsFromAssembly();

WebApplication app = builder.Build();
ResourceService service = app.Services.GetRequiredService<ResourceService>();

app.UseCors();

app.MapGet("/api/meta", () => Results.Json(service.GetMeta()));
app.MapGet("/api/items", (
    string? q,
    bool? produced,
    bool? used,
    int? limit,
    int? offset,
    string? lang) => Results.Json(service.ListItems(q, produced, used, limit ?? 100, offset ?? 0, lang)));
app.MapGet("/api/items/{itemId}", (string itemId, string? lang) =>
{
    try
    {
        return Results.Json(service.GetItemDetail(itemId, lang));
    }
    catch (DataNotFoundException)
    {
        return Results.Json(new Dictionary<string, object?> { ["error"] = "item_not_found" }, statusCode: 404);
    }
});
app.MapGet("/api/buildings", (string? lang) => Results.Json(service.ListBuildings(lang)));
app.MapGet("/api/corporations", (string? lang) => Results.Json(service.GetCorporations(lang)));
app.MapGet("/api/corporations/{corporationId}", (string corporationId, string? lang) =>
{
    try
    {
        return Results.Json(service.GetCorporationDetail(corporationId, lang));
    }
    catch (DataNotFoundException)
    {
        return Results.Json(new Dictionary<string, object?> { ["error"] = "corporation_not_found" }, statusCode: 404);
    }
});
app.MapGet("/api/planner/catalog", (string? lang) => Results.Json(service.GetPlannerCatalog(lang)));
app.MapGet("/api/planner/suggestions", (string? direction, [FromQuery(Name = "item_id")] string? itemId, string? lang) =>
{
    if (string.IsNullOrWhiteSpace(direction) || string.IsNullOrWhiteSpace(itemId))
    {
        return Results.Json(
            new Dictionary<string, object?> { ["error"] = "direction and item_id query parameters are required" },
            statusCode: 400);
    }

    Dictionary<string, object?> payload = service.GetPlannerSuggestions(direction, itemId, lang);
    return payload.ContainsKey("error")
        ? Results.Json(payload, statusCode: 400)
        : Results.Json(payload);
});
app.MapGet("/api/planner/transport-tiers", (string? lang) => Results.Json(service.GetTransportTiers(lang)));

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(settings.ItemAssetDir),
    RequestPath = "/assets/items",
});
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(settings.BuildingAssetDir),
    RequestPath = "/assets/buildings",
});

app.MapMcp("/mcp")
    .AllowAnonymous()
    .RequireCors(OpenMcpCorsPolicy);
app.Run($"http://{settings.Host}:{settings.Port}");
