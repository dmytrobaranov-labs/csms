using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

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

// Liveness: the process is up and serving HTTP. Runs no checks, so a database outage
// never makes an orchestrator restart this process.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

// Readiness: dependencies are reachable, so the instance can take traffic.
// The default writer returns plain "Healthy"/"Unhealthy" (503) without exception details.
var readiness = new HealthCheckOptions { Predicate = check => check.Tags.Contains(readyTag) };
app.MapHealthChecks("/health/ready", readiness);
app.MapHealthChecks("/health", readiness);

app.Run();
