using Crawlers.Server.Hubs;
using Crawlers.Server.Lobbies;
using Crawlers.Server.Logic;
using Crawlers.Server.Persistence;
using Crawlers.Server.Sessions;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<SessionManager>();
builder.Services.AddSingleton<LobbyManager>();
builder.Services.AddSingleton<SessionBroadcaster>();
builder.Services.AddSingleton<MovementService>();
builder.Services.AddSingleton<EngagementService>();
builder.Services.AddSingleton<CombatService>();
builder.Services.AddSingleton<RunEndService>();
builder.Services.AddSingleton<CombatRunner>();
builder.Services.AddSingleton<DescendService>();
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
}
else
{
    builder.Services.AddSingleton<IRunHistoryService, NullRunHistoryService>();
    builder.Services.AddSingleton<ICorpseService, NullCorpseService>();
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

app.UseCors(GameCorsPolicy);

app.MapGet("/", () => "Crawlers server up. SignalR hubs at /lobby and /game.");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapHub<LobbyHub>("/lobby");
app.MapHub<GameHub>("/game");

app.Run();

public partial class Program { }
