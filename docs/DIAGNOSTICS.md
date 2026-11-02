# Diagnostics

## Local stack

Start PostgreSQL, Seq and Jaeger:

```sh
docker compose up -d
docker compose ps        # postgres and seq should report "healthy"
```

Defaults live in `docker-compose.yml`; override them by copying `.env.example` to `.env`.
All ports bind to `127.0.0.1` only.

| Tool | URL | Used for |
|---|---|---|
| Seq UI | http://localhost:5380 | Search structured logs |
| Seq ingestion | http://localhost:5341 | Serilog sink target |
| Jaeger UI | http://localhost:16686 | Search traces |
| OTLP gRPC | http://localhost:4317 | OpenTelemetry exporter target |
| OTLP HTTP | http://localhost:4318 | OpenTelemetry exporter target |

Seq starts without login (`SEQ_NO_AUTH=true`). That setting is read only on the first start;
to change it, run `docker compose down -v` (this deletes the Seq and PostgreSQL data).

Jaeger keeps traces in memory, so they are gone after `docker compose restart jaeger`.

## Health checks

`Csms.Host` reads the PostgreSQL connection string from `ConnectionStrings:Postgres`.
It never comes from `appsettings.json`. Without it, the host refuses to start.
`.env` only configures docker-compose, so the app needs its own copy. Use the port from `.env`:

```sh
dotnet user-secrets set "ConnectionStrings:Postgres" \
  "Host=localhost;Port=5432;Database=csms;Username=csms;Password=csms_dev" --project src/Csms.Host
# or, for one shell session:
export ConnectionStrings__Postgres="Host=localhost;Port=5432;..."
```

User-secrets are loaded only in the `Development` environment (the default `dotnet run` profile).

| Endpoint | Checks | Use it as |
|---|---|---|
| `/health/live` | nothing: the process answers HTTP | liveness probe (restart if it fails) |
| `/health/ready` | PostgreSQL (`SELECT 1`, 5 s timeout) | readiness probe (stop routing traffic if it fails) |
| `/health` | same as `/health/ready` | alias for humans and simple monitors |

Responses are plain text: `Healthy` (200) or `Unhealthy` (503). Failure details never go into the
response. They go to the log as an error from `DefaultHealthCheckService` (event id 103), with the check name and exception.

```sh
curl -i http://localhost:5104/health/ready
docker compose stop postgres     # ready -> 503 Unhealthy, live stays 200 Healthy
docker compose start postgres
```

See [Health checks in ASP.NET Core](https://learn.microsoft.com/aspnet/core/host-and-deploy/health-checks).

## Logs

`Csms.Host` logs through Serilog. Every environment writes to the console; `Development` also
sends events to Seq at `http://localhost:5341` (`appsettings.Development.json`).
Open the Seq UI at http://localhost:5380.

### Log level

Levels come from the `Serilog` section of `appsettings.json` and can be overridden with
environment variables, without a rebuild:

```sh
export Serilog__MinimumLevel__Default=Debug
export Serilog__MinimumLevel__Override__Microsoft.AspNetCore=Information
```

### Request logging

Every HTTP request produces one completion event:
`HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed} ms`.
Levels: `Error` for exceptions and 5xx, `Debug` for `/health/*` (probes would otherwise flood Seq),
`Information` for everything else. A failing readiness probe is still visible: it returns 503, so it is logged as `Error`.

Headers and bodies are never logged. The `Authorization` header in particular must not be logged,
because stations send Basic Auth credentials in it from M1.

### Correlation ID

Every request gets a correlation ID. It is added to every log event written while the request is served
(property `CorrelationId`) and returned in the `X-Correlation-ID` response header.

- If the client sends `X-Correlation-ID` and the value is 1–64 characters of `[A-Za-z0-9._-]`, it is kept.
- Anything else (missing, too long, spaces, newlines, non-ASCII) is replaced by a new GUID (`N` format).
  Validation keeps clients from forging log lines or stuffing huge values into the logs.

```sh
curl -i -H "X-Correlation-ID: my-test-1" http://localhost:5104/health/ready
```

Then search in Seq:

| Query | Finds |
|---|---|
| `CorrelationId = 'my-test-1'` | everything logged for that request |
| `StatusCode >= 500` | failed requests |
| `@Level = 'Error'` | all errors |
| `RequestPath like '/health%'` | health probes (only stored at `Debug`) |

## Smoke tests

Check that Seq accepts a log event (CLEF format,
[docs](https://docs.datalust.co/docs/posting-raw-events)):

```sh
curl -s -X POST "http://localhost:5341/api/events/raw?clef" \
  -H "Content-Type: application/vnd.serilog.clef" \
  -d '{"@t":"2026-01-01T00:00:00Z","@mt":"Smoke test from {Source}","Source":"curl"}'
```

Then search for `Source = 'curl'` in the Seq UI.

Check that Jaeger accepts a span over OTLP/HTTP
([spec](https://opentelemetry.io/docs/specs/otlp/#otlphttp)):

```sh
curl -s -X POST http://localhost:4318/v1/traces -H "Content-Type: application/json" -d '{
  "resourceSpans": [{
    "resource": {"attributes": [{"key": "service.name", "value": {"stringValue": "smoke-test"}}]},
    "scopeSpans": [{"spans": [{
      "traceId": "5b8efff798038103d269b633813fc60c",
      "spanId": "eee19b7ec3c1b174",
      "name": "smoke",
      "kind": 1,
      "startTimeUnixNano": "1700000000000000000",
      "endTimeUnixNano": "1700000001000000000"
    }]}]
  }]
}'
```

Then pick the `smoke-test` service in the Jaeger UI, or query
`http://localhost:16686/api/traces/5b8efff798038103d269b633813fc60c`.
