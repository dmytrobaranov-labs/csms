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
