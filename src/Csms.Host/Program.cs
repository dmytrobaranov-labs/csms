using Csms.Host.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// Serilog replaces the default logging providers; ILogger<T> keeps working everywhere.
// Levels and sinks come from the "Serilog" configuration section, so they can change per
// environment (e.g. Serilog__MinimumLevel__Default=Debug) without a rebuild.
// No static Log.Logger: integration tests run many hosts in one process, and each host
// must own its logger.
builder.Services.AddSerilog(
    (services, logger) => logger
        .ReadFrom.Configuration(services.GetRequiredService<IConfiguration>())
        // Picks up any ILogEventSink registered in DI; the tests use this to capture events.
        .ReadFrom.Services(services)
        // Adds properties pushed via LogContext, such as CorrelationId.
        .Enrich.FromLogContext(),
    preserveStaticLogger: true);

// Comes from the environment (ConnectionStrings__Postgres) or user-secrets, never appsettings.json.
// Fail at startup: a missing setting is a deployment error, not something /health should discover later.
const string postgresKey = "ConnectionStrings:Postgres";
var postgresConnectionString = builder.Configuration[postgresKey];
if (string.IsNullOrWhiteSpace(postgresConnectionString))
{
    throw new InvalidOperationException(
        $"Configuration value '{postgresKey}' is missing. Set the environment variable " +
        "ConnectionStrings__Postgres or use dotnet user-secrets (see docs/DIAGNOSTICS.md).");
}

const string readyTag = "ready";
builder.Services.AddHealthChecks()
    // Runs "SELECT 1". The timeout keeps /health/ready from hanging when packets are dropped
    // rather than refused.
    .AddNpgSql(postgresConnectionString, name: "postgres", tags: [readyTag], timeout: TimeSpan.FromSeconds(5));

var app = builder.Build();

// First, so every later middleware and the request completion event see the CorrelationId.
app.UseMiddleware<CorrelationIdMiddleware>();

// One event per request: method, path, status code, elapsed time. It never logs headers or
// bodies, and must not be changed to (Authorization carries station credentials from M1).
app.UseSerilogRequestLogging(options =>
{
    // Without this, request logging writes to the static Log.Logger, which we never configure.
    options.Logger = app.Services.GetRequiredService<Serilog.ILogger>();
    options.GetLevel = RequestLogLevel;
});

// Liveness: the process is up and serving HTTP. Runs no checks, so a database outage
// never makes an orchestrator restart this process.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

// Readiness: dependencies are reachable, so the instance can take traffic.
// The default writer returns plain "Healthy"/"Unhealthy" (503) without exception details.
var readiness = new HealthCheckOptions { Predicate = check => check.Tags.Contains(readyTag) };
app.MapHealthChecks("/health/ready", readiness);
app.MapHealthChecks("/health", readiness);

app.Run();

// Health probes hit the service every few seconds; at Debug they stay out of Seq by default.
static LogEventLevel RequestLogLevel(HttpContext context, double elapsedMs, Exception? error) =>
    error is not null || context.Response.StatusCode >= 500 ? LogEventLevel.Error
    : context.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Debug
    : LogEventLevel.Information;
