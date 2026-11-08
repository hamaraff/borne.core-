using System.Security.Cryptography;
using System.Text;
using borne.business.Services;
using borne.core.IInfra;
using borne.device.api;
using borne.local.infrastructure;

if (args.Contains("--health-probe", StringComparer.Ordinal))
{
    try
    {
        using var probeClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        using HttpResponseMessage response = await probeClient.GetAsync("http://127.0.0.1:8080/health/ready");
        if (!response.IsSuccessStatusCode) Environment.Exit(1);
    }
    catch
    {
        Environment.Exit(1);
    }

    return;
}

var builder = WebApplication.CreateBuilder(args);

string borneId = RequiredSetting(builder.Configuration, "BORNE_ID");
if (borneId.Length > 128 ||
    !char.IsAsciiLetterOrDigit(borneId[0]) ||
    borneId.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '-')))
{
    throw new InvalidOperationException("BORNE_ID must be a stable 1-128 character identifier using letters, digits, dot, underscore, or dash.");
}

string apiKey = RequiredSecret("BORNE_API_KEY");
string cardHmacKey = RequiredSecret("BORNE_CARD_HMAC_KEY");
string dataPath = builder.Configuration["BORNE_DATA_PATH"] ??
    Path.Combine(builder.Environment.ContentRootPath, "data", "borne.db");
int port = builder.Configuration.GetValue("BORNE_PORT", 8080);
if (port is < 1 or > 65535) throw new InvalidOperationException("BORNE_PORT is invalid.");

string? centralBaseUrl = builder.Configuration["CENTRAL_API_BASE_URL"];
string? centralToken = Environment.GetEnvironmentVariable("CENTRAL_API_TOKEN");
bool simulationEnabled = builder.Configuration.GetValue("BORNE_SIMULATION_ENABLED", false);
int simulationIntervalSeconds = builder.Configuration.GetValue("BORNE_SIMULATION_INTERVAL_SECONDS", 5);
decimal simulationIncrement = builder.Configuration.GetValue("BORNE_SIMULATION_INCREMENT", 0.5m);
if (simulationIntervalSeconds <= 0 || simulationIncrement <= 0)
{
    throw new InvalidOperationException("Borne simulation interval and increment must be positive.");
}

if (string.Equals(apiKey, cardHmacKey, StringComparison.Ordinal))
{
    throw new InvalidOperationException("BORNE_API_KEY and BORNE_CARD_HMAC_KEY must be different secrets.");
}

if (!string.IsNullOrWhiteSpace(centralBaseUrl))
{
    if (!Uri.TryCreate(centralBaseUrl, UriKind.Absolute, out Uri? centralUri) ||
        centralUri.Scheme is not ("http" or "https"))
    {
        throw new InvalidOperationException("CENTRAL_API_BASE_URL must be an absolute HTTP(S) URL.");
    }

    if (string.IsNullOrWhiteSpace(centralToken) || Encoding.UTF8.GetByteCount(centralToken) < 32)
    {
        throw new InvalidOperationException("CENTRAL_API_TOKEN must be set to a value of at least 32 UTF-8 bytes when the central API URL is configured.");
    }

    if (string.Equals(centralToken, apiKey, StringComparison.Ordinal) ||
        string.Equals(centralToken, cardHmacKey, StringComparison.Ordinal))
    {
        throw new InvalidOperationException("The central token must be different from this Borne's other secrets.");
    }
}

builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new BorneRuntimeOptions(
    borneId, centralBaseUrl, centralToken,
    simulationEnabled, simulationIntervalSeconds, simulationIncrement));
builder.Services.AddSingleton(new SqliteBorneLocalStore(dataPath));
builder.Services.AddSingleton<IBorneLocalStore>(services => services.GetRequiredService<SqliteBorneLocalStore>());
builder.Services.AddSingleton<ICardTagHasher>(_ => new HmacCardTagHasher(cardHmacKey));
builder.Services.AddSingleton<BorneOperationService>();
builder.Services.AddSingleton<CentralLinkStatus>();
builder.Services.AddHostedService<LocalStoreStartup>();
if (!string.IsNullOrWhiteSpace(centralBaseUrl))
{
    builder.Services.AddHostedService<CentralWebSocketWorker>();
}
if (simulationEnabled)
{
    builder.Services.AddHostedService<ConsumptionSimulator>();
}

var app = builder.Build();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/health"))
    {
        await next();
        return;
    }

    string? provided = context.Request.Headers.Authorization.FirstOrDefault();
    if (provided?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) != true)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    byte[] expectedBytes = Encoding.UTF8.GetBytes(apiKey);
    byte[] providedBytes = Encoding.UTF8.GetBytes(provided[7..]);
    if (expectedBytes.Length != providedBytes.Length ||
        !CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    await next();
});

app.UseExceptionHandler(handler => handler.Run(async context =>
{
    if (context.Response.HasStarted) return;
    Exception? exception = context.Features
        .Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested) return;
    (int status, string code, string title) = exception switch
    {
        UnauthorizedAccessException => (401, "unauthorized", "The card is not authorized."),
        ArgumentException => (400, "validation_error", "The request is invalid."),
        KeyNotFoundException => (404, "not_found", "The requested local record was not found."),
        InvalidOperationException => (409, "business_rule_violation", "The request conflicts with Borne state."),
        _ => (500, "internal_error", "The request could not be completed.")
    };
    context.Response.StatusCode = status;
    context.Response.ContentType = "application/problem+json";
    await context.Response.WriteAsJsonAsync(new
    {
        status,
        code,
        title,
        traceId = context.TraceIdentifier
    });
}));

app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
app.MapGet("/health/ready", async (IBorneLocalStore store, CancellationToken cancellationToken) =>
    await store.IsAvailableAsync(cancellationToken)
        ? Results.Ok(new { status = "7ather" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));
app.MapBorneLocalApi();
app.Run();

static string RequiredSetting(IConfiguration configuration, string key)
{
    string? value = configuration[key];
    return !string.IsNullOrWhiteSpace(value)
        ? value.Trim()
        : throw new InvalidOperationException($"{key} must be provided through runtime configuration.");
}

static string RequiredSecret(string key)
{
    string? value = Environment.GetEnvironmentVariable(key);
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException($"{key} must be provided through a protected runtime secret source.");
    }

    if (Encoding.UTF8.GetByteCount(value) < 32)
    {
        throw new InvalidOperationException($"{key} must contain at least 32 UTF-8 bytes.");
    }

    return value;
}

public partial class Program;
