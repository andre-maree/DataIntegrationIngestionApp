using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;

namespace DataIntegrationIngestionApp.Services;

/// <summary>
/// Resolves the raw JSON payload for an ingestion message, whether the data is
/// carried inline in the message body or referenced in blob storage.
/// </summary>
public interface IPayloadReader
{
    /// <summary>
    /// Returns the raw JSON payload as a string.
    /// </summary>
    /// <param name="message">The received Service Bus message.</param>
    /// <param name="isBlobReference">True when the body is a blob reference rather than inline data.</param>
    Task<string> ReadPayloadAsync(
        ServiceBusReceivedMessage message,
        bool isBlobReference,
        CancellationToken cancellationToken = default);
}
