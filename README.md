# Data Integration Ingestion App

A .NET 8 isolated-worker Azure Functions app that ingests batches of records from a
**session-enabled Azure Service Bus queue** and performs a **SQL Server bulk insert**,
with layered retries and strict per-table FIFO ordering.

See [`docs/IngestionPlan.md`](docs/IngestionPlan.md) for the full design.

## Projects

- **DataIntegrationIngestionApp** — the Functions app (Service Bus trigger + SQL bulk insert).
- **DataIntegrationIngestionApp.AppHost** — .NET Aspire host that runs the Azure Service Bus
  **emulator** locally with a session-enabled `ingestion-queue`.

## Prerequisites

Install these before running the app after cloning:

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (running) — the AppHost starts the
  Service Bus emulator and a SQL Server container.
- [Azure Functions Core Tools v4](https://learn.microsoft.com/azure/azure-functions/functions-run-local)
  (`func`) — required to launch the Functions project from the AppHost.

## How it works

- Each message carries a **`SessionId` = schema-qualified table name** (e.g. `dbo.Contacts`).
  Service Bus sessions guarantee strict FIFO per table; different tables process in parallel.
- **Payload routing** by `ContentType`:
  - `application/json` — inline JSON array in the message body.
  - `application/vnd.ingestion.blobref+json` — body is `{ "container": "...", "blob": "..." }`
	and the data is downloaded from blob storage.
- **Bulk insert** into the `SessionId` table via `SqlBulkCopy` inside a transaction.

### Layered retries

1. **Layer 1 — SqlClient built-in transient retry** (seconds): absorbs transient SQL errors
   inside the invocation.
2. **Layer 2 — bounded in-process backoff** (capped exponential) before releasing the message,
   throttling redeliveries while the session lock auto-renews.
3. **Layer 3 — abandon → in-order redelivery**: the same batch is retried first (FIFO preserved)
   until it succeeds or reaches **`MaxDeliveryCount`** (configurable, default **10**), after which
   it is dead-lettered.

> A permanently failing batch intentionally blocks **only its own table's session** until it
> succeeds or dead-letters. Monitor stalled sessions and the dead-letter queue.

## Configuration (`local.settings.json` / app settings)

| Key | Description |
| --- | --- |
| `ServiceBusConnection` | Service Bus connection (injected by Aspire locally). |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | App Insights connection string. |
| `Ingestion:MaxDeliveryCount` | Max deliveries before dead-letter (default 10). |
| `Ingestion:SqlConnectionString` | **SQL Server connection string (provide before running SQL insert).** |
| `Ingestion:BlobStorageConnectionString` | Blob storage connection for large payloads. |
| `Ingestion:SqlRetry:*` | Layer 1 transient retry tuning. |
| `Ingestion:Backoff:*` | Layer 2 backoff tuning (`BaseDelaySeconds`, `MaxDelaySeconds`). |

Keep `host.json` `maxAutoLockRenewalDuration` greater than the worst-case attempt time
(Layer 1 + Layer 2 delay) so the session lock is not lost mid-retry.

## Running locally

The `local.settings.json` for the Functions project is committed (it contains only non-secret
placeholders), so no extra setup is needed. In Visual Studio, ensure
**DataIntegrationIngestionApp.AppHost** is the startup project and press **F5**.

From the CLI:

```powershell
dotnet run --project DataIntegrationIngestionApp.AppHost
```

> If you need to add secrets (e.g. a real `APPLICATIONINSIGHTS_CONNECTION_STRING`), edit
> `local.settings.json` locally — but avoid committing real secret values.

The AppHost starts the Service Bus emulator and the Functions app, injecting the
`ServiceBusConnection` connection string.

## Telemetry & Alerting (Application Insights)

The function emits the following custom telemetry:

| Signal | Type | When |
| --- | --- | --- |
| `BatchIngested` | Event | Batch successfully inserted (includes `Table`, `RecordCount`). |
| `BatchRetryScheduled` | Event | Batch failed and will be retried in place (includes `DeliveryCount`). |
| `BatchDeadLettered` | Event | Batch reached `MaxDeliveryCount` and was dead-lettered. |
| `SessionRetryDepth` | Metric | Current `DeliveryCount` for a failing batch (surfaces stalled sessions). |
| Exceptions | Exception | Any processing failure, with `Table`/`SessionId` dimensions. |

### Recommended Azure Monitor alert rules (create in the portal / IaC)

1. **Function exceptions** — alert when exception count on the function is `> 0` over a short window.
2. **Dead-letters** — alert when the `BatchDeadLettered` custom-event count is `> 0`.
3. **Stalled sessions** — alert when the `SessionRetryDepth` metric stays high
   (e.g. `>= MaxDeliveryCount - 2`) for a sustained period.
4. Optionally, an alert on the queue's **dead-letter message count** at the Service Bus namespace.

## Sample messages

See [`docs/sample-messages.md`](docs/sample-messages.md) for inline and blob-reference examples.
