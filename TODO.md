# TODO — Data Integration Ingestion App

Backlog of improvements identified during the PoC review. Grouped by theme, roughly in
priority order. Check items off as they land.

## 0. Upstream design decisions (DO THESE FIRST — block section 1)
These need to be settled before implementing the generic/dynamic schema, because they
change what the deserializer and inserter should even produce.

### 0a. Persistence mechanism — is `SqlBulkCopy` still the right tool?
- [ ] Re-evaluate `SqlBulkCopy` vs. alternatives now that dynamic schema + idempotency
      are on the roadmap (e.g. bulk-copy-into-staging + `MERGE`, table-valued parameters,
      `MERGE`/upsert directly, or a bulk library).
- [ ] Decide based on: idempotency needs, per-table upsert keys, throughput, and how
      well each option handles a dynamic column set.
- [ ] Outcome feeds section 1 (dynamic schema) and section 2 (idempotency).

### 0b. Admin-configurable table column mapping
- [ ] Design a per-table mapping an admin sets up: source (JSON/payload) field ->
      destination column, plus type / nullable / required info.
- [ ] Decide where the mapping lives (config file, DB table/registry, blob, etc.) and
      how it is loaded / cached / refreshed at runtime.
- [ ] Decide precedence: admin mapping vs. reflecting the live SQL table schema.
- [ ] Consider validation, defaults, unknown-column handling, and mapping versioning.

## 1. Dynamic / generic schema (make ingestion table-agnostic)
Currently the pipeline is `Contact`-specific despite the "any `SessionId` table" design.

- [ ] Generalize `SqlBulkInserter.BuildDataTable` so columns are not hardcoded to
	  `Contact` (`Id, Name, Surname, Age, Email`).
- [ ] Replace the `Contact`-typed `IRecordDeserializer` with a schema-agnostic
	  representation (e.g. deserialize to a column/value map or `DataTable`).
- [ ] Decide how the target schema is discovered (message metadata vs. reading SQL
	  table schema vs. a config/registry per table).
- [ ] Validate/coerce incoming payload columns against the destination table.

## 2. Idempotency / exactly-once (no duplicates on redelivery)
Crash after `CommitAsync` but before `CompleteMessageAsync` currently re-inserts the batch.

- [ ] Introduce a staging-table + `MERGE` (or `INSERT ... WHERE NOT EXISTS`) pattern:
	  `SqlBulkCopy` into staging, then `MERGE` into the target on a business key.
- [ ] Make the merge/business key configurable per table.
- [ ] Consider a processed-message/batch dedup ledger keyed by `MessageId`.

## 3. Retry / backoff strategy at scale
Layer 2 uses in-process `Task.Delay` (up to `MaxDelaySeconds = 300`) while holding the
session lock — costs execution time and risks the function timeout ceiling.

- [ ] Evaluate deferred/scheduled redelivery instead of sleeping in-process.
- [ ] Review interaction with Consumption plan function timeout vs. `MaxDelaySeconds`.
- [ ] Ensure `host.json` `maxAutoLockRenewalDuration` covers worst-case attempt time.

## 4. Poison vs. transient error handling
Non-transient errors (bad payload / validation) currently retry the full
`MaxDeliveryCount` before dead-lettering.

- [ ] Fail fast on deserialization/validation errors (dead-letter immediately) instead
	  of retrying like a transient SQL fault.

## 5. Cleanups / polish
- [ ] Reconsider `SqlBulkCopy` `BulkCopyTimeout = 0` (no timeout can hang under contention).
- [ ] Add a "PoC scope & limitations" note to `README.md` (points 1–4 above).
- [ ] Add unit tests around the injected services (`IPayloadReader`,
	  `IRecordDeserializer`, `ISqlBulkInserter`, `IRetryDelayPolicy`).

## Notes / decisions
- `SqlBulkCopy` chosen for fast append-style load; the idempotency upgrade (section 2)
  is intended to build on it via bulk-copy-into-staging + `MERGE`, not replace it.
