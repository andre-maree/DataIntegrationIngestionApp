# Sample Ingestion Messages

Both message types target the `dbo.Contacts` table. Set **`SessionId = "dbo.Contacts"`**
on every message so batches for that table process strictly FIFO.

## 1. Inline payload

- **SessionId**: `dbo.Contacts`
- **ContentType**: `application/json`
- **Body** (JSON array of records):

```json
[
  { "id": 1, "name": "Ada",   "surname": "Lovelace", "age": 36, "email": "ada@example.com" },
  { "id": 2, "name": "Alan",  "surname": "Turing",   "age": 41, "email": "alan@example.com" },
  { "id": 3, "name": "Grace", "surname": "Hopper",   "age": null, "email": null }
]
```

## 2. Blob-reference payload (large data)

- **SessionId**: `dbo.Contacts`
- **ContentType**: `application/vnd.ingestion.blobref+json`
- **Body** (pointer to the blob holding the JSON array):

```json
{ "container": "ingestion", "blob": "contacts/2026-07-30/batch-001.json" }
```

The referenced blob's content must itself be a JSON array of records identical in shape to
the inline example above.

## Sending a test message (Azure CLI example)

```powershell
# Inline message with a session id
az servicebus queue message send `
  --namespace-name <namespace> `
  --queue-name ingestion-queue `
  --body '[{"id":1,"name":"Ada","surname":"Lovelace","age":36,"email":"ada@example.com"}]' `
  --content-type application/json `
  --session-id dbo.Contacts
```

> When running locally through the Aspire AppHost, use the Service Bus emulator endpoint /
> a client (e.g. Azure Service Bus Explorer or a small sender program) and be sure to set the
> **SessionId** and **ContentType** as shown above.
