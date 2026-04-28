using Crawlers.Generation.Scaling;
using Crawlers.Generation.Weapons;
using Crawlers.Server.Config;
using Crawlers.Server.Hubs;
using Crawlers.Server.Lobbies;
using Crawlers.Server.Logic;
using Crawlers.Server.Persistence;
using Crawlers.Server.Sessions;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Difficulty curve. Loaded once at startup from Config/floor-scaling.json
// and held as a singleton; DescendService consults it on every floor mint.
// Editable in-repo and tunable by restart — no live reload.
var floorScalingPath = Path.Combine(builder.Environment.ContentRootPath, "Config", "floor-scaling.json");
var floorScalingTable = FloorScalingLoader.LoadFromFile(floorScalingPath);
builder.Services.AddSingleton(floorScalingTable);

// Step 3.4 weapon catalogue. Same edit-and-restart contract as the
// floor-scaling table; ChestService draws weapon names from a floor's
// weaponLoot pool and resolves the WeaponDefinition through this
// registry to stamp the dropped Item.Weapon block.
var weaponsPath = Path.Combine(builder.Environment.ContentRootPath, "Config", "weapons.json");
var weaponRegistry = WeaponRegistryLoader.LoadFromFile(weaponsPath);
builder.Services.AddSingleton(weaponRegistry);

builder.Services.AddSingleton<SessionManager>();
builder.Services.AddSingleton<LobbyManager>();
builder.Services.AddSingleton<SessionBroadcaster>();
builder.Services.AddSingleton<MovementService>();
builder.Services.AddSingleton<EngagementService>();
builder.Services.AddSingleton<CombatService>();
builder.Services.AddSingleton<RunEndService>();
builder.Services.AddSingleton<CombatRunner>();
builder.Services.AddSingleton<DescendService>();
builder.Services.AddSingleton<ChestService>();
builder.Services.AddSignalR();

// Persistence — wire DbContext + RunHistoryService when a connection string
// is configured. Without one, fall back to a no-op so the server still runs
// for local dev (`dotnet run` outside docker, no Postgres).
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var hasDb = !string.IsNullOrWhiteSpace(connectionString);
if (hasDb)
{
    builder.Services.AddDbContext<CrawlersDbContext>(options =>
        options.UseNpgsql(connectionString));
    builder.Services.AddSingleton<IRunHistoryService, RunHistoryService>();
    builder.Services.AddSingleton<ICorpseService, CorpseService>();
    builder.Services.AddSingleton<IPlayerIdentityService, PlayerIdentityService>();
    builder.Services.AddSingleton<IFloorWorldService, FloorWorldService>();
    builder.Services.AddSingleton<IWorldStatsService, WorldStatsService>();
}
else
{
    builder.Services.AddSingleton<IRunHistoryService, NullRunHistoryService>();
    builder.Services.AddSingleton<ICorpseService, NullCorpseService>();
    builder.Services.AddSingleton<IPlayerIdentityService, NullPlayerIdentityService>();
    builder.Services.AddSingleton<IFloorWorldService, NullFloorWorldService>();
    builder.Services.AddSingleton<IWorldStatsService, NullWorldStatsService>();
}

const string GameCorsPolicy = "GameClient";

// Cors:AllowedOrigins comes from configuration (appsettings.json or env var
// Cors__AllowedOrigins). Comma-separated list of fully-qualified origins.
// Empty / unset → no CORS allowance (fail closed).
var allowedOrigins = (builder.Configuration["Cors:AllowedOrigins"] ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
{
    options.AddPolicy(GameCorsPolicy, policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
    });
});

var app = builder.Build();

if (hasDb)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CrawlersDbContext>();
    await db.Database.MigrateAsync();
    app.Logger.LogInformation("Database migrations applied");
}
else
{
    app.Logger.LogWarning(
        "ConnectionStrings:DefaultConnection is empty — run history will not be persisted.");
}

// World mint runs after migrations apply (so the floors table exists) and
// before any hub starts accepting connections. Idempotent — re-mint only
// happens when WorldConstants.Version has been bumped past what's in the
// rows; otherwise existing floors are loaded from the DB into the in-
// memory cache for fast per-session lookups.
{
    var worldService = app.Services.GetRequiredService<IFloorWorldService>();
    await worldService.MintAsync(WorldConstants.InitialFloorCount);
}

app.UseCors(GameCorsPolicy);

app.MapGet("/", () => "Crawlers server up. SignalR hubs at /lobby and /game.");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
// Step 12: aggregate world stats. Public, anonymous — no Identify
// required, anyone can hit the URL and see the graveyard's totals.
app.MapGet("/api/world-stats", async (IWorldStatsService stats, CancellationToken ct) =>
{
    var dto = await stats.GetGlobalStatsAsync(ct);
    return Results.Ok(dto);
});
app.MapHub<LobbyHub>("/lobby");
app.MapHub<GameHub>("/game");

app.Run();

public partial class Program { }
