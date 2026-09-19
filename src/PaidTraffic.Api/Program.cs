using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PaidTraffic.Api;
using PaidTraffic.Api.Integration;
using PaidTraffic.Api.Operations;
using PaidTraffic.Api.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => { options.IncludeScopes = true; options.UseUtcTimestamp = true; options.TimestampFormat = "O"; });
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddDbContextFactory<TrafficDbContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("Traffic") ?? throw new InvalidOperationException("ConnectionStrings:Traffic is required.")));
builder.Services.AddScoped<IGoogleAdsClient, SimulatedGoogleAdsClient>();
builder.Services.AddScoped<TrafficWorkflow>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Telemetry>();
builder.Services.AddHostedService<EvaluationWorker>();
var otel = builder.Services.AddOpenTelemetry().ConfigureResource(r => r.AddService(Telemetry.Name))
    .WithTracing(t => t.AddAspNetCoreInstrumentation().AddSource(Telemetry.Name))
    .WithMetrics(m => m.AddAspNetCoreInstrumentation().AddMeter(Telemetry.Name));
if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
{
    otel.WithTracing(t => t.AddOtlpExporter());
    otel.WithMetrics(m => m.AddOtlpExporter());
}

var app = builder.Build();
var demo = app.Configuration.GetValue<bool>("Demo:Enabled");
var apiKey = app.Configuration["Security:ApiKey"];
if (!demo && string.IsNullOrWhiteSpace(apiKey))
    throw new InvalidOperationException("Security:ApiKey is required outside explicit demo mode.");
if (app.Configuration.GetValue("Evaluation:IntervalSeconds", 60) < 1)
    throw new InvalidOperationException("Evaluation interval must be positive.");
// The host deliberately only enables the simulator. The SDK adapter is compiled but not registered.
if (app.Configuration.GetValue("GoogleAds:Mode", "Simulated") != "Simulated")
    throw new InvalidOperationException("This demo host supports only Simulated mode. Wire and validate the SDK adapter before live use.");
if (args.Contains("--migrate", StringComparer.Ordinal))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<TrafficDbContext>();
    await db.Database.MigrateAsync();
    if (demo) await DemoSeed.InitializeAsync(db, CancellationToken.None);
    return;
}
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    if (!demo && context.Request.Path.StartsWithSegments("/api"))
    {
        var supplied = Encoding.UTF8.GetBytes(context.Request.Headers["X-Api-Key"].ToString());
        if (!CryptographicOperations.FixedTimeEquals(supplied, Encoding.UTF8.GetBytes(apiKey!)))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
    }
    await next(context);
});
app.MapOpenApi();
app.MapGet("/health", async (TrafficDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "healthy" }) : Results.StatusCode(503));
app.MapTrafficEndpoints(demo);
await app.RunAsync();

public partial class Program;
