# Borne Device Runtime

The active solution builds one Borne runtime image per Borne plus a separate central persistence API project. Each Borne container has its own local API, SQLite data volume, and configurable host port. The central API owns MySQL persistence and accepts Borne events; it does not participate in local session calculations.

## Architecture

- `borne.core`: domain types and provider-neutral Borne local-storage contracts.
- `borne.business`: reservation, card authorization, session, cumulative consumption, and progressive tariff rules.
- `borne.local.infrastructure`: per-Borne SQLite database, immutable tariff snapshots, HMAC card-tag storage, and transactional sync outbox.
- `borne.device.api`: authenticated local HTTP API, central WebSocket client, heartbeat, and optional consumption simulator.
- `borne.central.api`: authenticated central WebSocket endpoint, event persistence/acknowledgement, heartbeat health, configuration delivery, and monitoring updates.

The obsolete `borne.api` and `borne.api.tests` projects have been removed. `borne.central.api` is the central persistence host and uses `borne.infrastructure` for MySQL access; the Borne device image does not reference either central project.

The older DAO-based CRUD services and `I*DAO` contracts are also retained in `borne.business` / `borne.core` for that legacy code. The per-Borne API uses `BorneOperationService` and `IBorneLocalStore`; it does not register the legacy CRUD services or connect to MySQL.

## Local Docker simulation

Generate distinct runtime keys in PowerShell and start two independent containers. These values stay in the shell environment and are not written into the repository or image:

```powershell
function New-BorneSecret {
  $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
  $bytes = New-Object byte[] 32
  $rng.GetBytes($bytes)
  $value = [Convert]::ToBase64String($bytes)
  $rng.Dispose()
  return $value
}
$env:BORNE_A_API_KEY = New-BorneSecret
$env:BORNE_A_CARD_HMAC_KEY = New-BorneSecret
$env:BORNE_B_API_KEY = New-BorneSecret
$env:BORNE_B_CARD_HMAC_KEY = New-BorneSecret
docker compose up --build
```

The containers bind to `127.0.0.1:5001` and `127.0.0.1:5002`; both listen on port `8080` internally. Named volumes `borne_a_data` and `borne_b_data` keep their SQLite state separate and durable across container restarts. Both Bornes attach to the named private `borne_network`, which the separate central API deployment can join. The Compose simulator enables a cumulative reading every five seconds while a session is running. Pausing a session stops the simulator; authorizing the same card resumes the cached reservation while it remains valid.

Set each `BORNE_*_CENTRAL_API_TOKEN` separately and `CENTRAL_API_BASE_URL` when central connectivity is enabled. The Borne requires WSS/HTTPS for remote deployments. Without a central URL, local operation and the durable outbox still work; pending events remain queued.

To run the central service in this Compose network, apply `borne.infrastructure/Database/001-initial-schema.sql` and then `borne.infrastructure/Database/002-central-api-persistence.sql` to MySQL. Generate separate runtime tokens for each Borne, monitoring, and tariff management (all at least 32 UTF-8 bytes), then set `BORNE_DB_CONNECTION_STRING`, `BORNE_A_CENTRAL_API_TOKEN`, `BORNE_B_CENTRAL_API_TOKEN`, `CENTRAL_API_MONITORING_TOKEN`, `CENTRAL_API_TARIFF_MANAGEMENT_TOKEN`, and `CENTRAL_API_BASE_URL=http://central-api:8080` in the shell. Start with `docker compose --profile central up --build`. The central API is available locally at `http://127.0.0.1:5080`; its container listens on internal port 8080 and binds the host port to loopback.

## Central tariff management API

Tariff management routes require `Authorization: Bearer <tariff-management-token>`. The central API calls the existing `TarifService` and `TrancheService` for CRUD and validation, persists changes through their MySQL DAO contracts, then pushes the resulting tariff configuration immediately to every connected Borne. The response includes `configurationFramesSent`, the number of configuration frames sent. A disconnected Borne receives the current configuration on its next WebSocket connection.

| Method | Central URL | Purpose |
|---|---|---|
| GET | `/api/tariffs` | List tariffs |
| POST | `/api/tariffs` | Add a tariff |
| PUT | `/api/tariffs/{tarifId}` | Change the tariff energy type |
| DELETE | `/api/tariffs/{tarifId}` | Delete a tariff (database foreign keys may require deleting its tranches first) |
| GET | `/api/tariffs/{tarifId}/tranches` | List its tranches |
| POST | `/api/tranches` | Add a tranche |
| PUT | `/api/tranches/{trancheId}` | Modify a tranche |
| DELETE | `/api/tranches/{trancheId}` | Delete a tranche |

Example tranche request body for `POST /api/tranches` and `PUT /api/tranches/{trancheId}`:

```json
{
  "tarifId": 7,
  "minimum": 0,
  "maximum": 100,
  "price": 0.25
}
```

The database and Borne model support one tariff per energy type. A usable tariff must have contiguous bands beginning at zero. Empty or incomplete schedules are removed from the active local selection; immutable snapshots still remain available to sessions already using them. A schedule edit creates a new fingerprinted version. Running sessions stay pinned to their previous snapshot, and new sessions use the newly synchronized version.

## Local API and Postman

All operational routes require `Authorization: Bearer <borne-api-key>`. `/health/live` and `/health/ready` are public status-only routes.

| Method | Local URL | Purpose |
|---|---|---|
| GET | `/health/live` | Process liveness |
| GET | `/health/ready` | SQLite readiness |
| GET | `/api/borne/status` | Identity, central-link status, cached reservation/session summary |
| PUT | `/api/tariffs/cache` | Cache a tariff configuration/version |
| PUT | `/api/reservations/cache` | Cache the single reservation delivered to this Borne |
| DELETE | `/api/reservations/cache/{reservationId}` | Clear the matching cached reservation |
| POST | `/api/sessions/start` | Authenticate the reservation card and start or resume its session |
| POST | `/api/sessions/pause` | Pause consumption while retaining the reservation |
| POST | `/api/sessions/resume` | Reauthorize and resume a paused session |
| POST | `/api/consumption/readings` | Submit a cumulative meter total (useful for Postman/manual simulation) |
| GET | `/api/consumption/current` | Latest cumulative reading and price fields |
| GET | `/api/sync/pending` | Number of queued events awaiting central acknowledgement |

Cache a tariff before starting a session:

```json
{
  "tarifId": 7,
  "energyType": "Electricite",
  "version": "tariff-2026-10-v1",
  "bands": [
    { "id": 1, "minimum": 0, "maximum": 100, "price": 0.25 },
    { "id": 2, "minimum": 100, "maximum": 500, "price": 0.32 }
  ]
}
```

Cache a reservation. The `cardTag` is HMAC-hashed before it is written to SQLite and is never returned by the local API:

```json
{
  "reservationId": 42,
  "referenceId": 1001,
  "startsAtUtc": "2026-10-31T08:00:00Z",
  "endsAtUtc": "2026-10-31T10:00:00Z",
  "energyType": "Electricite",
  "cardTag": "test-card-tag"
}
```

Then `POST /api/sessions/start` with `{ "cardTag": "test-card-tag" }`. To submit a cumulative meter reading, `POST /api/consumption/readings` with `{ "cumulativeQuantity": 12.5 }`. A tariff crossing is priced progressively; the reading’s `amount` is the charge since the previous reading, while `cumulativeAmount` is the total charge at the current meter value. The central API must persist both values and deduplicate by event/reading ID.

## Central API WebSocket contract

The Borne opens `ws[s]://{central-host}/ws/bornes/{borneId}` with `Authorization: Bearer <per-borne-central-token>`. Frames are UTF-8 JSON envelopes with `event`, `version` (`1`), `messageId`, `sentAtUtc`, and `data`.

- The Borne sends `heartbeat` every 10 seconds with `{ borneId, sentAtUtc }`. The central API replies `heartbeat.ack`; it should mark a Borne offline after its heartbeat timeout.
- Reservations are created/cached directly through the Borne's own authenticated HTTP API. The central persistence API does not create, replace, or clear Borne reservations.
- The central API sends `tariff.configuration` with `{ tarifId, energyType, version, bands: [{ id, minimum, maximum, price }] }`. The Borne persists the immutable version and replies `tariff.configuration.ack`.
- When a tariff is deleted or no longer has a valid contiguous schedule, the central API sends `tariff.configuration.removed`. The Borne deactivates it for new sessions but retains immutable snapshots needed by existing sessions.
- The Borne sends pending outbox events (`consumption.reading`, `session.started`, `session.resumed`, `session.paused`, or `session.cancelled`) in the standard envelope. Event `messageId` is the durable event ID. Consumption data includes `borneId`, `readingId`, `reservationId`, `occurredAtUtc`, `cumulativeQuantity`, `amount` (incremental charge), `cumulativeAmount`, `tarifId`, and `tariffVersion`.
- The central API replies `sync.ack` with `{ eventId }` only after durable persistence. The Borne then removes that event from SQLite. If the connection drops before acknowledgement, the Borne resends the same ID after reconnect; the central API must make persistence idempotent.

The central API persists each Borne event as an immutable JSON record keyed by the durable event ID, verifies duplicate payloads, then acknowledges it. Event history is available at `GET /api/bornes/{borneId}/events?limit=100`; latest heartbeat health is available at `GET /api/bornes/{borneId}/health`. These read routes require the monitoring bearer token. On connection, the API sends tariff configurations loaded from MySQL. Tariff versions include a fingerprint of the configured bands so changed prices produce a new immutable version.

Monitoring clients connect to `/ws/monitoring` with `Authorization: Bearer <monitoring-token>` and receive a recent persisted event/health replay on connect, followed by live event and heartbeat updates. The replay includes up to the latest 500 events; clients can request older per-Borne history through the protected history route. Health is considered online while the last heartbeat is less than 35 seconds old. The Borne outbox provides replay after Borne reconnection; no local session or calculation depends on WebSocket availability.

## Secrets and deployment notes

- Each Borne needs a stable `BORNE_ID`, a unique `BORNE_API_KEY`, a unique `BORNE_CARD_HMAC_KEY`, and a per-Borne `CENTRAL_API_TOKEN` when connected centrally. Secrets must be at least 32 UTF-8 bytes and distinct from each other.
- Supply secrets at runtime using the deployment secret store or protected process environment. Never pass secrets as Docker build arguments or commit them in `.env`, compose, source, logs, or image layers.
- The runtime image uses pinned .NET 10 patch tags and runs as a non-root user. Keep the SQLite file on its dedicated persistent volume.
- Compose binds host ports only to loopback for local testing. For nonlocal deployment, configure network access and TLS/WSS deliberately; do not expose a Borne API port publicly without a protected ingress.
- Card tags are stored locally as keyed HMAC-SHA256 values. The HMAC key must remain available across container restarts and key rotation must preserve any active reservation’s ability to authenticate.

## Build and current limits

Build the complete solution, including both API hosts, with `dotnet build borne.core.slnx`. Central API operation requires the MySQL schema scripts above and runtime secrets. The Borne continues operating from SQLite and queues data while the central API or database is unavailable.
