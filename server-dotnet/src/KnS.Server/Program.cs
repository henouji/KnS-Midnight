using KnS.Data;
using KnS.Game;
using KnS.Platform;
using KnS.Server.Admin;
using KnS.Server.Configuration;
using KnS.Server.Diagnostics;
using KnS.Server.Endpoints;
using KnS.Server.Routing;
using Microsoft.Extensions.Logging.Console;

// KnS mock backend: the game gateway, the Com2uS platform surfaces and the addition-file CDN.
// Run from the repository root (work/ and original/ are relative to it):
//   dotnet run --project server-dotnet/src/KnS.Server -- 8080
ServerOptions serverOptions = EnvironmentSettings.ReadServerOptions(args);
DatabaseOptions databaseOptions = EnvironmentSettings.ReadDatabaseOptions();
GameOptions gameOptions = EnvironmentSettings.ReadGameOptions();

WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
builder.WebHost.UseUrls($"http://0.0.0.0:{serverOptions.Port}");
builder.WebHost.ConfigureKestrel(kestrel => kestrel.AddServerHeader = false);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(console =>
{
    console.SingleLine = true;
    console.TimestampFormat = "HH:mm:ss ";
    console.ColorBehavior = LoggerColorBehavior.Disabled;
});
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

builder.Services
    .AddSingleton(serverOptions)
    .AddKnSData(databaseOptions)
    .AddKnSGame(gameOptions)
    .AddKnSPlatform()
    .AddSingleton<RequestJournal>()
    .AddSingleton<TrafficLog>()
    .AddSingleton<HealthCheck>()
    .AddSingleton<AdditionFileEndpoint>()
    .AddSingleton<DownloadPageEndpoint>()
    .AddSingleton<SignupEndpoint>()
    .AddSingleton<TesterAdminEndpoint>()
    .AddSingleton<AccountAdminEndpoint>()
    .AddSingleton<FeatureFlagAdminEndpoint>()
    .AddScoped<AdminWork>()
    .AddScoped<TesterAdminService>()
    .AddScoped<AccountAdminService>()
    .AddScoped<FeatureFlagAdminService>()
    .AddSingleton<RequestRouter>();

WebApplication app = builder.Build();

// Create the schema and load the master tables before the first request.
GameDatabase database = app.Services.GetRequiredService<GameDatabase>();
database.OpenSession().Dispose();
app.Logger.LogInformation("KnS server listening on 0.0.0.0:{Port} (db={Database})", serverOptions.Port, database.Description);

app.MapMethods("/{**path}", [HttpMethods.Get, HttpMethods.Post],
    (HttpContext context, RequestRouter router) => router.RouteAsync(context));

app.Run();

/// <summary>The entry point; public so integration tests can host the app (WebApplicationFactory).</summary>
public partial class Program;
