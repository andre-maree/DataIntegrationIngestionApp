// Sends a single Service Bus message containing 3 inline Contact records to the
// "ingestion-queue" queue, using the schema-qualified destination table as the SessionId,
// matching the format expected by DataImportQueueTriggerFunction (inline flow).

using System.Text.Json;
using Azure.Messaging.ServiceBus;

const string queueName = "ingestion-queue";
const string destinationTable = "dbo.Contacts";
const string inlineContentType = "application/json";

// Provide the Service Bus connection string via the SERVICEBUS_CONNECTION_STRING
// environment variable, or fall back to the fixed local emulator connection string
// (matches the pinned host port configured in AppHost.cs).
const string defaultConnectionString =
    "Endpoint=sb://localhost:59234;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;;EntityPath=ingestion-queue";

var connectionString = Environment.GetEnvironmentVariable("SERVICEBUS_CONNECTION_STRING")
    ?? defaultConnectionString;

if (string.IsNullOrWhiteSpace(connectionString) || connectionString.StartsWith("<"))
{
    Console.Error.WriteLine(
        "Set the SERVICEBUS_CONNECTION_STRING environment variable to a valid Service Bus connection string.");
    return 1;
}

var namePool = new[]
{
    ("Jane", "Doe"), ("John", "Smith"), ("Alice", "Brown"), ("Bob", "Johnson"), ("Carol", "Williams"),
    ("David", "Jones"), ("Emma", "Garcia"), ("Frank", "Miller"), ("Grace", "Davis"), ("Henry", "Rodriguez"),
};

var contacts = BuildContacts(1, 3);
var json = JsonSerializer.Serialize(contacts);

await using var client = new ServiceBusClient(connectionString);
await using var sender = client.CreateSender(queueName);

while (true)
{
    Console.Write("Type 'e' to exit, 'c' to send 10 batches of 3 rows, or press Enter to send another batch: ");
    var input = Console.ReadLine();

    if (string.Equals(input, "e", StringComparison.OrdinalIgnoreCase))
    {
        return 0;
    }

    if (string.Equals(input, "c", StringComparison.OrdinalIgnoreCase))
    {
        // Build all 10 payloads (ids 1-30) first, then send them.
        var messages = new List<ServiceBusMessage>();
        var nextId = 1;

        for (var batch = 0; batch < 10; batch++)
        {
            var batchContacts = BuildContacts(nextId, 3);
            nextId += batchContacts.Length;

            messages.Add(new ServiceBusMessage(JsonSerializer.Serialize(batchContacts))
            {
                ContentType = inlineContentType,
                // SessionId is the schema-qualified destination table; the queue is session-enabled.
                SessionId = destinationTable,
            });
        }

        foreach (var batchMessage in messages)
        {
            await sender.SendMessageAsync(batchMessage);
        }

        Console.WriteLine($"Sent {messages.Count} messages with 3 row(s) each (ids 1-{nextId - 1}) to '{queueName}' (session '{destinationTable}').");
        continue;
    }

    var message = new ServiceBusMessage(json)
    {
        ContentType = inlineContentType,
        // SessionId is the schema-qualified destination table; the queue is session-enabled.
        SessionId = destinationTable,
    };

    await sender.SendMessageAsync(message);

    Console.WriteLine($"Sent 1 message with {contacts.Length} row(s) to '{queueName}' (session '{destinationTable}').");
}

object[] BuildContacts(int startId, int count)
{
    var result = new object[count];

    for (var i = 0; i < count; i++)
    {
        var id = startId + i;
        var (name, surname) = namePool[(id - 1) % namePool.Length];

        result[i] = new
        {
            id,
            name,
            surname,
            age = 25 + (id % 40),
            email = $"{name.ToLowerInvariant()}.{surname.ToLowerInvariant()}{id}@example.com",
        };
    }

    return result;
}
