# CLAUDE.md — CSMS on OCPP 1.6J

## Project
A Charging Station Management System (CSMS) for EV chargers, speaking OCPP 1.6J over WebSocket.
It is a portfolio and learning project. The owner is preparing for .NET, L2/L3 support and SRE interviews,
so understanding matters more than speed: every change must be explainable.

The task list (milestones M0–M7 and INC) is the source of truth for scope. Do one task item at a time.

## Stack
- .NET 10, ASP.NET Core, C# latest
- PostgreSQL 16 via EF Core: stations, sessions, tariffs
- MongoDB: OCPP message log, meter values
- RabbitMQ via RabbitMQ.Client (from M3)
- Serilog → Seq (logs), OpenTelemetry → Jaeger (traces), System.Diagnostics.Metrics (metrics)
- xUnit, Testcontainers for integration tests
- Next.js dashboard (from M2)
- Everything local runs through docker-compose

## Solution layout
```
src/
  Csms.Host          ASP.NET Core host: WebSocket endpoint, REST API, DI, diagnostics setup
  Csms.Ocpp          OCPP frames, messages, parser, dispatcher contracts
  Csms.Domain        entities and business rules
  Csms.Infrastructure EF Core, MongoDB, RabbitMQ
  Csms.Emulator      console charge point emulator with load profiles
tests/
  Csms.UnitTests
  Csms.IntegrationTests
docs/
  ARCHITECTURE.md, DIAGNOSTICS.md, adr/, incidents/
```

## Dependency rules
- Csms.Ocpp depends on nothing else in the solution.
- Csms.Domain does not reference EF Core, MongoDB or RabbitMQ.
- Csms.Infrastructure depends on Domain; Host wires everything together.
- Check with `dotnet list reference` after changing references.

## Do not use
- MediatR, AutoMapper, MassTransit
- Third-party OCPP libraries. The protocol core (WebSocket loop, frame parser, dispatcher,
  request/response correlation) is written in this repo. Payload records generated from the
  official JSON schemas are allowed (see "OCPP contracts").
- Secrets or connection strings in appsettings.json: use environment variables or user-secrets

## Architecture notes
- Stations connect via WebSocket at `/ocpp/{chargePointId}` with the `ocpp1.6` subprotocol.
- OCPP-J frames: `[2, id, Action, payload]` CALL, `[3, id, payload]` CALLRESULT,
  `[4, id, ErrorCode, description, details]` CALLERROR. Both sides can send requests;
  responses are matched by message id.
- One WebSocket connection per station; the connection registry lives in memory.
  Horizontal scaling (sticky routing, backplane) is deliberately out of scope for the MVP.
- From M3, background consumers run in a separate `Csms.Workers` process in the same solution.

## Domain conventions
- Energy is an integer number of Wh (`long`). Convert to kWh only at the UI and report edge.
- All times are UTC. Use `DateTimeOffset`, store as `timestamptz` in PostgreSQL.
  Never call `DateTime.Now` / `DateTime.UtcNow` directly: inject `TimeProvider`, so tests can control time.
- Connection strings come from environment variables or user-secrets, never from appsettings.json.

## OCPP contracts
- The official OCPP 1.6 JSON schemas (draft-04) for the Core profile live in `src/Csms.Ocpp/Schemas/`.
- C# payload records may be generated from these schemas.
- Contract tests are written by hand: every outgoing payload is validated against its official schema
  (e.g. with NJsonSchema).
- Do not commit the specification PDFs; the README links to the Open Charge Alliance website.
- Open question for M1: what the CSMS does when an incoming payload violates a schema constraint
  (e.g. `chargePointVendor` longer than 20 characters). Decide it in an ADR before implementing.

## ADR index (docs/adr/)
- 0001 OCPP 1.6J, Core profile (2.0.1 in the backlog)
- 0002 Modular monolith, one connection per station, in-memory registry
- 0003 PostgreSQL for relational data, MongoDB for the message log and meter values
- 0004 EF Core for writes, Dapper for reads
- 0005 RabbitMQ messaging (M3)
- 0006 OCPP JSON schemas and contract tests

## Engineering principles
Adapted from Robert C. Martin's ub-mode (https://github.com/unclebob/ub-mode).
They apply to every change except incident mode (see below).

- **TDD.** Write the failing test before changing the code. See it fail for the reason you expect,
  then make the smallest change that makes it pass. If an honest test is impractical, say so
  instead of writing a bad one.
- **Boundary discipline.** Validate external data where it enters (WebSocket frames, OCPP payloads,
  REST requests, configuration). Parse it once into typed values; inside the system, trust it.
  Keep business logic in pure functions where practical.
- **Make illegal states impossible.** Model the domain with types that cannot hold invalid values
  (e.g. connector status as a closed type, not a string; energy as Wh, not a free-form number).
  Do not silence the compiler or nullable warnings.
- **Idempotency.** Running an operation twice, or again after a crash halfway through, ends in the
  same state. This matters most for OCPP retries and RabbitMQ consumers.
- **Fix root causes.** Reproduce the problem first, then fix its cause. A guard that only hides
  the crash is not a fix.
- **Test behavior, not implementation.** Tests call the code the way its user does and check what
  that user would see. If a test would still pass when every function returned nothing, delete it.
- **Small verifiable units.** Finish one small unit, prove it (build, tests), then start the next.
  Each commit says why, what changed, and how it was verified. Changes reach main only through a PR.
- **Quality gates (from end of M1).** Coverage via coverlet + ReportGenerator: keep the CRAP score of
  each method under 30 in Risk Hotspots, aim for under 10 in Csms.Ocpp and Csms.Domain.
  Mutation testing via Stryker.NET on Csms.Ocpp and Csms.Domain: cover what is uncovered,
  kill survivors, delete dead code.

## Work modes
Each task item in the task list has a tag. Follow it.
- **Claude Code**: implement it fully on your own. Keep the diff focused on that item.
- **AI + review**: implement it, keep it small, and in the PR description explain every non-obvious line
  (why this API, what happens on failure). The owner will read every line.
- **by hand**: do NOT write the code. Answer questions, give hints, review the owner's code, point to the spec section.

Before a non-trivial change, show a short plan and wait for approval.

## Diagnostics (required for every feature)
- Structured logs with message templates, never string interpolation:
  `logger.LogInformation("Boot from {ChargePointId}", id)`
- Every OCPP message is logged with ChargePointId, Action and MessageId as properties.
- Correlation ID flows through logs and traces.
- New code paths get an OpenTelemetry span where it helps an investigation.
- Metrics use the `csms.` prefix, e.g. `csms.stations.connected`, `csms.ocpp.handler.duration`.
- Document new diagnostic commands in docs/DIAGNOSTICS.md.

## Tests
- Unit tests for parsing and domain rules; integration tests via WebApplicationFactory and Testcontainers.
- A bug fix comes with a test that fails before the fix.
- `dotnet build` and `dotnet test` must pass before you say a task is done.

## Git
- Branch per task: `task/NNN-short-name`.
- Conventional commits: `feat:`, `fix:`, `test:`, `docs:`, `chore:`.
- PR description: what changed, how to verify it locally, the answer to the task's check question if there is one.
- Never commit to main directly.

## Incident mode
Triggered only when the owner says "start incident NNN" (optionally with a theme such as "memory" or "database").
1. Create branch `incident/NNN` from main.
2. Inject ONE realistic fault that a real team could ship. It must compile, pass existing tests,
   and show up under an emulator load profile within ~30 minutes.
3. Commit it with a plausible, neutral message that does not reveal the fault.
4. Write `docs/incidents/NNN/ticket.md` in a customer's voice: symptom, when it started, impact. No hints about the cause.
5. Write the root cause, affected files and the intended detection path to `docs/incidents/NNN/answer.md`.
   This file is git-ignored.
6. In the chat, show ONLY the ticket and which load profile to run. Never show the diff, never name the files,
   never hint at the cause.
7. If the owner asks for a hint, give one level at a time:
   level 1 = which signal to look at (logs, traces, metrics, dump); level 2 = which component; level 3 = which tool command.
8. When the owner says "reveal NNN", compare their postmortem with answer.md and point out what they missed.
Never inject faults outside incident mode, and never into main.
The engineering principles do not apply to the injected fault itself, but they do apply to the
owner's fix: the fix starts with a test that reproduces the incident.

## Communication
- Write code comments, docs, commit messages and PR descriptions in plain English.
- Prefer short explanations with a link to the OCPP spec section or Microsoft docs.
