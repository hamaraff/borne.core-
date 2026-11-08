using System.Data.Common;
using System.Text;
using borne.central.api;
using borne.core.IInfra;
using borne.infrastructure.Data;
using borne.infrastructure.Daos;

if (args.Contains("--health-probe", StringComparer.Ordinal))
{
    try
    {
        string probePort = Environment.GetEnvironmentVariable("CENTRAL_API_PORT") ?? "8080";
        using var probeClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        using HttpResponseMessage response = await probeClient.GetAsync($"http://127.0.0.1:{probePort}/health/ready");
        if (!response.IsSuccessStatusCode) Environment.Exit(1);
    }
    catch { Environment.Exit(1); }
    return;
}

var builder = WebApplication.CreateBuilder(args);
string? connectionString = builder.Configuration["BORNE_DB_CONNECTION_STRING"];
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("BORNE_DB_CONNECTION_STRING must be supplied through protected runtime configuration.");
}

int port = builder.Configuration.GetValue("CENTRAL_API_PORT", 8080);
if (port is < 1 or > 65535) throw new InvalidOperationException("CENTRAL_API_PORT is invalid.");
IConfigurationSection borneTokens = builder.Configuration.GetSection("CentralApi:BorneTokens");
string[] configuredTokens = borneTokens.GetChildren().Select(token => token.Value ?? string.Empty).ToArray();
if (configuredTokens.Length == 0 || configuredTokens.Any(token => Encoding.UTF8.GetByteCount(token) < 32) ||
    configuredTokens.Distinct(StringComparer.Ordinal).Count() != configuredTokens.Length)
{
    throw new InvalidOperationException("Configure unique per-Borne tokens of 32 or more UTF-8 bytes.");
}
string? monitoringToken = builder.Configuration["CentralApi:MonitoringToken"];
if (string.IsNullOrWhiteSpace(monitoringToken) || Encoding.UTF8.GetByteCount(monitoringToken) < 32)
{
    throw new InvalidOperationException("CentralApi:MonitoringToken must be provided as a protected runtime secret of 32 or more UTF-8 bytes.");
}
if (configuredTokens.Contains(monitoringToken, StringComparer.Ordinal))
{
    throw new InvalidOperationException("The monitoring token must be distinct from every Borne token.");
}
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);
builder.Services.AddSingleton(new MySqlConnectionFactory(connectionString));
builder.Services.AddScoped<IConfigDAO, MySqlConfigBorneDAO>();
builder.Services.AddScoped<IConsommationDAO, MySqlConsommationDAO>();
builder.Services.AddScoped<ITarifDAO, MySqlTarifDAO>();
builder.Services.AddScoped<ITrancheDAO, MySqlTrancheDAO>();
builder.Services.AddSingleton<CentralPersistenceStore>();
builder.Services.AddSingleton<CentralRealtimeHub>();
builder.Services.AddSingleton<BorneTokenValidator>();

var app = builder.Build();
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    if (context.Response.HasStarted) return;
    Exception? exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested) return;
    (int status, string code) = exception switch
    {
        ArgumentException => (400, "validation_error"),
        KeyNotFoundException => (404, "not_found"),
        InvalidOperationException => (409, "conflict"),
        DbException => (503, "storage_unavailable"),
        _ => (500, "internal_error")
    };
    context.Response.StatusCode = status;
    context.Response.ContentType = "application/problem+json";
    await context.Response.WriteAsJsonAsync(new { status, code, traceId = context.TraceIdentifier });
}));
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
app.MapGet("/health/live", () => Results.Ok(new { status = "central api yekhdem jawo behi" }));
app.MapGet("/health/ready", async (CentralPersistenceStore store, CancellationToken ct) =>
    await store.IsReadyAsync(ct) ? Results.Ok(new { status = "central api 7ather" }) : Results.StatusCode(503));
app.MapCentralApi();
app.Run();

public partial class Program;
