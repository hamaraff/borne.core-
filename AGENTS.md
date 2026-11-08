# Borne.Core Repository Instructions

## Project Goal

Maintain a .NET solution for simulating independent energy charging stations (bornes), including local reservations, consumption, tariffs, and central persistence. Preserve current behavior and ask for requirements when a change depends on unstated policy.

## Architecture Boundaries

- `borne.core/` is the .NET 10 domain library: entities, enums, and DAO contracts. Keep it independent of database providers and application workflows.
- `borne.business/` targets .NET 10 and contains application services. Put business orchestration here; depend on core contracts rather than provider-specific implementations.
- `borne.infrastructure/` targets .NET 10 and owns the MySQL integrations used by the central persistence API.
- `borne.central.api/` is the .NET 10 ASP.NET Core central persistence host and WebSocket boundary.
- `borne.local.infrastructure/` owns per-Borne SQLite persistence and the durable event outbox.
- `borne.device.api/` is the per-Borne HTTP API and central WebSocket client.
- `borne.connectiontest/` is a small connectivity utility, not a home for domain or production service logic.

Keep dependencies flowing from infrastructure and business toward core abstractions. All projects target .NET 10 with nullable reference types enabled. Do not add packages without an explicit task requirement.

## Dependency Injection and Configuration

Business services use constructor injection. API hosts compose concrete implementations with the built-in DI container; keep the domain and service libraries provider-neutral. The central API reads `BORNE_DB_CONNECTION_STRING` through .NET configuration. The connection utility reads it from the process environment. Do not store credentials in `appsettings.json`.

For a local PowerShell connection check, enter the connection string at the prompt rather than saving it in source: `$env:BORNE_DB_CONNECTION_STRING = Read-Host 'Database connection string'`, then run `dotnet run --project borne.connectiontest/borne.connectiontest.csproj`. Clear it afterward with `Remove-Item Env:BORNE_DB_CONNECTION_STRING`.

## Security

Treat database credentials and connection strings as secrets. Never commit real credentials, paste them into examples, or include them in logs or error output. Use local secret configuration or environment-specific settings; keep machine-local files out of version control. Before changing configuration, check that no secret is being exposed.

## Required Task-Start Procedure

Before implementing each task:

1. Read this file and any more-specific `AGENTS.md` files that apply to the files being changed.
2. Identify the relevant skill and read its `.agents/skills/<skill-name>/SKILL.md` before implementation. If no skill applies, continue using these repository instructions.
3. Inspect the relevant code and current working-tree changes. Preserve unrelated user changes.
4. Confirm scope and constraints from the task; do not infer undocumented business rules.

## Build and Validation

Build with `dotnet build borne.core.slnx`. There is currently no automated test project in the active solution. Add a focused Borne test project when requested and keep live-MySQL integration checks separately identifiable. Run validation relevant to the requested work and report what was run.

## Borne Runtime Architecture Update (2026-10-31)

The active solution builds one Borne runtime per container and a separate central persistence API. `borne.core/` contains provider-neutral domain/contracts; `borne.business/` contains local reservation/session/tariff rules; `borne.local.infrastructure/` owns each Borne's SQLite store and durable outbox; `borne.device.api/` is the per-Borne HTTP API and central WebSocket client; `borne.central.api/` persists events and heartbeats; and `borne.infrastructure/` provides the central API's MySQL adapters. The obsolete `borne.api/` and `borne.api.tests/` projects have been removed. `borne.connectiontest/` remains a standalone connectivity utility excluded from the active solution. Do not connect the Borne image directly to central MySQL.

For the current architecture, each Borne manages at most one reservation through its own local API, honors only a cached reservation while offline and only until its end time, and pins an immutable tariff version to each session. The central API sends tariff configuration but does not create or clear Borne reservations. Consumption quantities are cumulative meter totals. Store the per-reading charge delta and cumulative charge separately. Persist each reading, the session checkpoint, and its outbox event atomically; delete the event only after a central `sync.ack`.

The active Borne settings are runtime-only: `BORNE_ID`, `BORNE_API_KEY`, `BORNE_CARD_HMAC_KEY`, and (when configured) `CENTRAL_API_BASE_URL` and a unique per-Borne `CENTRAL_API_TOKEN`. Keep secrets out of image layers and source. The image must run non-root and use a separate durable SQLite volume per container. See `README.md` for Postman routes and the central WebSocket event contract. For the active solution, build with `dotnet build borne.core.slnx`.
