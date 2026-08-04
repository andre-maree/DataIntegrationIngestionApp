# Data Ingestion Function: Session-FIFO Service Bus Trigger + SQL Bulk Insert with Layered Retries

## Context & Findings
Current state:
- `DataImportQueueTriggerFunction` — isolated-worker Service Bus trigger on queue `ingestion-queue`, `Connection = ""`. Logs and completes only. **No sessions yet.**
- `Program.cs` — minimal isolated-worker host with App Insights already wired (`AddApplicationInsightsTelemetryWorkerService` + `ConfigureFunctionsApplicationInsights`), no other DI.
- `.csproj` — .NET 8, isolated worker (`Microsoft.Azure.Functions.Worker` 2.51.0), Service Bus extension 5.22.2. No blob/SQL/Aspire packages.
- No Aspire AppHost project in the `.slnx` solution — net-new.

## Decisions
- **Inline vs blob**: distinguished by `message.ContentType` (`application/json` = inline; `application/vnd.ingestion.blobref+json` = blob reference).
- **Payload format**: JSON array of records.
- **Aspire scope**: Service Bus emulator only for now; blob + SQL via config.
- **FIFO requirement**: **Service Bus Sessions, one session per SQL table.** `SessionId` = **schema-qualified table name** (e.g. `dbo.Contacts`). Strict, absolute FIFO — later batches for a tabl[...]
- **Long-term failure handling**: **block the session and retry the failing batch in place** (abandon → in-order redelivery). Retries continue up to **`MaxDeliveryCount`**, then the batch is dea[...]
- **Short-term transient retries**: **`Microsoft.Data.SqlClient` built-in transient retry** (no Polly, no extra package).
- **Observability**: **basic App Insights alerting** (custom events/metrics + error telemetry) for failed batches, abandons/redeliveries, stalled sessions, and dead-letters.
- **Target SQL table (dev)**: `DemoDatabase.dbo.Contacts` — `Id INT NOT NULL`, `Name NVARCHAR(50) NOT NULL`, `Surname NVARCHAR(50) NOT NULL`, `Age INT NULL`, `Email NVARCHAR(320) NULL`.

## Retry Architecture (final)
Three cooperating layers, all preserving strict FIFO because a single session consumer processes one batch at a time and never advances past a failing batch:

1. **Layer 1 — SqlClient built-in transient retry (seconds).** Configure `SqlConnection` with a retry logic provider (`SqlRetryLogicOption`: retry count, delay, transient error numbers) so deadl[...]
2. **Layer 2 — bounded in-process delayed retry before releasing (throttle).** If Layer 1 still fails, apply a capped exponential backoff wait (grows to a configurable max, e.g. ~5 min) while th[...]
3. **Layer 3 — abandon → in-order redelivery, capped by `MaxDeliveryCount`.** After the delay, **abandon** the message. Because the queue is session-enabled, Service Bus redelivers the **same*...]

Rationale note: scheduled re-enqueue with backoff was rejected because completing + re-enqueuing a failed batch lets later batches jump ahead, violating strict per-table FIFO.

## Observability / Alerting (basic App Insights)
- App Insights is already registered in `Program.cs`; reuse it via `TelemetryClient` / `ILogger` structured logs.
- Emit custom telemetry:
  - `TrackEvent("BatchIngested", ...)` on success (session/table, record count).
  - `TrackEvent("BatchRetryScheduled", ...)` on each abandon with `DeliveryCount`, session, error.
  - `TrackException` on failures with session/table dimensions.
  - `TrackEvent("BatchDeadLettered", ...)` when `MaxDeliveryCount` reached.
  - Optional `TrackMetric("SessionRetryDepth", DeliveryCount)` to surface stalled sessions.
- Portal-side alert rules to create: (1) exceptions on the function, (2) `BatchDeadLettered` custom-event count > 0, (3) high `SessionRetryDepth`. Actual Azure alert resources are portal/IaC confi[...]

## Design
- **Models/`Contact.cs`**: maps to `dbo.Contacts` (`int Id`, `string Name`, `string Surname`, `int? Age`, `string? Email`).
- **Options/`IngestionOptions`**: bound from config — `MaxDeliveryCount` (default 10), Layer 1 retry settings, Layer 2 backoff cap.
- **Services/**:
  - `IPayloadReader` / `PayloadReader` — returns raw JSON string; inline from `message.Body`, blob-ref via `BlobServiceClient` (config `BlobStorage`).
  - `IRecordDeserializer` — `System.Text.Json` deserialize JSON array → `List<Contact>` (case-insensitive, validates required `Id`/`Name`/`Surname`).
  - `ISqlBulkInserter` / `SqlBulkInserter` — builds `DataTable` (`Id, Name, Surname, Age, Email`), `SqlBulkCopy` into destination table (from `SessionId`) within a transaction; `SqlConnection` c[...]
  - `IRetryDelayPolicy` — capped exponential backoff from `DeliveryCount` (Layer 2).
- **The session-enabled Service Bus trigger function**: `[ServiceBusTrigger("ingestion-queue", Connection = "ServiceBusConnection", IsSessionsEnabled = true)]`. Read `SessionId` (= table); route payload (inline/blob ...]
- **Content-type constants + classifier** for inline vs blob-reference.

## Session / Broker configuration
- Queue `ingestion-queue`: **`RequiresSession = true`**, `MaxDeliveryCount` set from the same configurable value (default 10).
- `host.json` `extensions.serviceBus`: `sessionIdleTimeout`, `maxConcurrentSessions`, `maxAutoLockRenewalDuration` (≥ worst-case Layer 1 + Layer 2 time), prefetch/one-at-a-time to guarantee orde[...]
- **Producers** (not built here) must set `SessionId = <schema.table>` and `ContentType` — documented.

## Packages to add
- `Azure.Storage.Blobs`
- `Microsoft.Data.SqlClient` (built-in transient retry — no Polly)

## Config
- Trigger `Connection = "ServiceBusConnection"`.
- `local.settings.json`: `ServiceBusConnection`, `SqlConnection` (DemoDatabase), `BlobStorage`, `Ingestion:MaxDeliveryCount` (10), Layer 1/Layer 2 retry tuning, App Insights connection string.

## Aspire (Service Bus emulator only, session-enabled)
- New `DataIntegrationIngestionApp.AppHost` referencing `Aspire.Hosting.Azure.ServiceBus`; `AddAzureServiceBus(...).RunAsEmulator()` with queue `ingestion-queue` configured **`RequiresSession = tr...]

## Risks / Open Items
- With `MaxDeliveryCount = 10`, a persistently-failing batch dead-letters after 10 in-order redeliveries — while stalled it blocks only its table's session (intentional strict FIFO). Ensure the [...]
- `maxAutoLockRenewalDuration` must exceed worst-case single-attempt time (Layer 1 + Layer 2 delay) or the lock is lost and the batch redelivers early (still ordered, but wasted work + faster Deli[...]
- `Id` is `NOT NULL`, non-identity — payload must supply `Id`.
- Blob reading needs Azurite/storage locally; supplied via `BlobStorage` config since Aspire is Service Bus-only.
- Verify the Service Bus emulator supports session-enabled queues in the current tooling.
- Multi-table support beyond `Contacts` needs a table→model/mapping registry (dev handles `Contacts` only).

## Steps
1. Add NuGet packages to `DataIntegrationIngestionApp.csproj` — `Azure.Storage.Blobs`, `Microsoft.Data.SqlClient`. (No Polly.)
2. Create `Models/Contact.cs` — maps to `dbo.Contacts` (`int Id`, `string Name`, `string Surname`, `int? Age`, `string? Email`).
3. Create `Options/IngestionOptions.cs` — configurable `MaxDeliveryCount` (default 10), Layer 1 retry settings, Layer 2 backoff cap; bind from configuration.
4. Create `Services/IPayloadReader.cs` + `PayloadReader.cs` — return raw JSON string; inline from `message.Body`, blob-ref via `BlobServiceClient`.
5. Create `Services/IRecordDeserializer.cs` + `RecordDeserializer.cs` — deserialize JSON array into `List<Contact>` with case-insensitive matching and required-field validation.
6. Create `Services/ISqlBulkInserter.cs` + `SqlBulkInserter.cs` — build `DataTable`, `SqlBulkCopy` into destination table (from `SessionId`) within a transaction; configure `SqlConnection` with [...]
7. Create `Services/IRetryDelayPolicy.cs` + implementation — capped exponential backoff from `DeliveryCount` (Layer 2).
8. Define content-type constants (inline vs blob-reference) + classifier helper.
9. Create/update the session-enabled Service Bus ingestion function — implement a Service Bus trigger with `IsSessionsEnabled = true` and `Connection = "ServiceBusConnection"`; read `SessionId` as the target table; route → deserialize → bulk insert; success completes; failure emits the retry/dead-letter signals described above.
10. Register services, `BlobServiceClient`, `IngestionOptions`, and SQL retry options in `Program.cs` DI; bind connections + retry tuning from configuration (App Insights already registered).
11. Add/adjust `host.json` — `extensions.serviceBus` session settings (`maxConcurrentSessions`, `sessionIdleTimeout`, `maxAutoLockRenewalDuration`, one-at-a-time prefetch).
12. Add `local.settings.json` entries — `ServiceBusConnection`, `SqlConnection` (DemoDatabase), `BlobStorage`, `Ingestion:MaxDeliveryCount=10`, retry tuning, App Insights connection string.
13. Create `DataIntegrationIngestionApp.AppHost` with `Aspire.Hosting.Azure.ServiceBus`; `RunAsEmulator()` with `ingestion-queue` session-enabled (`RequiresSession = true`, `MaxDeliveryCount = 10`...]
14. Document App Insights alert rules to create in the portal (function exceptions, `BatchDeadLettered` count, high `SessionRetryDepth`).
15. Provide sample messages (inline Contacts JSON array and a blob-reference message) each with `SessionId = "dbo.Contacts"` for local FIFO end-to-end testing.
