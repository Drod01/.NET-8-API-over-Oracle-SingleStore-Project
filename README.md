# Wellhead API — .NET 8 service over Oracle + SingleStore (MemSQL)

An ASP.NET Core minimal API and Kafka ingress service for oil & gas production data: **Oracle for
master data and stored forecasts, SingleStore for high-volume five-minute telemetry**, one domain
model across both, with the SQL dialects kept in a single auditable place.

```
                  ┌────────────────────────┐
   Kafka ────────▶│ TelemetryStreamConsumer│──┐
   well.telemetry │ validate → window      │  │  idempotent upsert
                  └────────────────────────┘  ▼
                                    ┌─────────────────────┐
 /api/wells        ────────────────▶│ SingleStore         │  production_windows
 /api/kpis         ◀────────────────│ columnstore,        │  anomaly_signals
 /api/alerts       ◀────────────────│ sharded by well     │
 /api/wells/{id}/forecast           └─────────────────────┘
        │
        ▼ Arps decline fit
 ┌─────────────────────┐
 │ Oracle              │  wells, well_kpi_daily, well_forecast
 │ MERGE upserts       │
 └─────────────────────┘
```

## Why this project exists

The role names **.NET, Oracle and MemSQL (SingleStore)**, plus event-driven ingress and containerised
deployment. This service is the polyglot-persistence half of the portfolio: the Python project builds
the stream, this one is the transactional read/write API and .NET consumer a product team would call.

| Requirement | Where it lives |
| --- | --- |
| .NET service design | `Program.cs`, `Endpoints/WellEndpoints.cs` (minimal API, DI, typed contracts) |
| Oracle | `Data/OracleWellRepository.cs`, `db/oracle/001_schema.sql` (MERGE upsert, constraints, FKs) |
| SingleStore / MemSQL | `Data/SingleStoreTelemetryRepository.cs`, `db/singlestore/001_schema.sql` (columnstore, shard + sort keys) |
| Two SQL dialects, one contract | `Data/SqlDialect.cs` — bind syntax, row limiting, upsert grammar per engine |
| Event-driven .NET | `Ingest/TelemetryStreamConsumer.cs` — offsets committed only after a successful batch |
| Streaming correctness | `Ingest/WindowAccumulator.cs` — event time, watermark, lateness accounting |
| Math model | `Domain/DeclineCurve.cs` — Arps exponential fit plus a residual-based P10/P50/P90 band |
| Data quality | `Ingest/TelemetryEvent.cs` — tolerant JSON parsing, physics validation |
| Testability | in-memory repositories for demo mode; 41 xUnit tests including HTTP integration tests |
| Containers | `Dockerfile` (multi-stage, non-root), `docker-compose.yml` (Oracle Free, SingleStore, Redpanda) |

## Quickstart — no database required

The API runs in **demo mode** when connection strings are absent: seeded in-memory repositories behind
the same interfaces, and `/health` reports `"mode": "demo"` so the data can never be mistaken for a
connected environment.

```bash
dotnet test                                    # 41 tests
dotnet run --project src/Wellhead.Api
curl -s localhost:5000/health | jq
curl -s localhost:5000/api/kpis | jq '.count'
curl -s "localhost:5000/api/wells/W-0001/forecast?days=7" | jq
```

Against the real engines:

```bash
docker compose up --build
curl -s localhost:8080/api/wells | jq 'length'
```

## Endpoints

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `/health` | liveness, deployment mode, model version, Kafka ingress state |
| GET | `/api/wells` | master data (Oracle) |
| GET | `/api/wells/{id}` | one well plus its window count |
| GET | `/api/wells/{id}/windows?limit=` | latest telemetry windows (SingleStore) |
| GET | `/api/wells/{id}/forecast?days=` | Arps fit + P10/P50/P90 projection |
| GET | `/api/kpis?limit=` | 24 h volume, rate, coverage and monthly decline per well |
| GET | `/api/alerts?hours=&limit=` | flagged windows for review |
| POST | `/api/windows` | validated batch upsert (partial batches report per-row errors) |

## The two-database decision, and what stays in each

- **Oracle** — low volume, relational, transactional: the asset hierarchy, daily KPI snapshots, stored
  forecasts. `MERGE` keeps loads idempotent; FK and CHECK constraints enforce that a forecast cannot
  point at a well that does not exist and that `p10 ≤ p50 ≤ p90`.
- **SingleStore** — columnstore, sharded by `well_id`, sorted by `window_start`: billions of five-minute
  windows, scanned by `WHERE well_id = ? ORDER BY window_start DESC LIMIT n`. The shard key is the well,
  so a well's history lives on one partition and time-range reads stay local.
- **Dialect isolation** — `TelemetrySql` is the only place that knows Oracle uses `:param` /
  `FETCH FIRST n ROWS ONLY` / `MERGE`, while SingleStore uses `@param` / `LIMIT` /
  `ON DUPLICATE KEY UPDATE`. Unit tests assert both outputs, so a change to one engine cannot silently
  break the other.

## Reliability details

- **Offsets are committed after the write**, not after the read. A crash mid-batch replays the batch;
  the upsert absorbs the replay. At-least-once delivery plus idempotent writes beats exactly-once
  theatre.
- **No silent event loss**: malformed or out-of-range readings are logged with the reason and skipped,
  and `late_dropped` / `closed_windows` are logged on shutdown.
- **Demo mode is explicit**: an empty connection string does not degrade quietly into fake production
  data — it sets a reported runtime mode used by integration tests.
- **Non-root container**, release build, no SDK in the runtime image.

## Verification status

`dotnet test` passes 41 tests: decline-curve math, windowing/watermarking, both SQL dialects, event
parsing and validation, KPI and forecast services, and HTTP integration tests that boot the real
`WebApplicationFactory` pipeline in demo mode.

The repositories are **compiled and unit-tested but not executed against live Oracle or SingleStore**
in this repository's CI — those engines require licensed/heavy containers. The DDL, bind syntax and
upsert grammar are covered by tests, and the compose file is provided for a full run. That distinction
is stated here rather than implied away.

## Layout

```
src/Wellhead.Api/
  Domain/       ProductionWindow, Well, WellKpi, DeclineCurve (math model)
  Data/         Dapper repositories, connection factories, SqlDialect builder, in-memory demo repos
  Ingest/       event contract + validator, window accumulator, Kafka BackgroundService
  Services/     KpiService, ForecastService
  Endpoints/    minimal API routes
  Demo/         deterministic seed data for demo mode
tests/Wellhead.Tests/   41 xUnit tests (unit + HTTP integration)
db/oracle/               Oracle DDL, constraints, comments
db/singlestore/          SingleStore columnstore DDL + rollup view
```
