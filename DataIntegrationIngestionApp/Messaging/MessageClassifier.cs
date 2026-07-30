using System;
using Azure.Messaging.ServiceBus;

namespace DataIntegrationIngestionApp.Messaging;

/// <summary>
/// Content-type constants used to distinguish inline payloads from blob references.
/// </summary>
public static class IngestionContentTypes
{
    /// <summary>Inline JSON payload carried directly in the message body.</summary>
    public const string Inline = "application/json";

    /// <summary>Body is a JSON blob reference ({ "container", "blob" }) for large payloads.</summary>
    public const string BlobReference = "application/vnd.ingestion.blobref+json";
}

/// <summary>
/// Classifies an ingestion message by its content type.
/// </summary>
public static class MessageClassifier
{
    /// <summary>
    /// Returns true when the message body is a blob reference rather than inline data.
    /// </summary>
    public static bool IsBlobReference(ServiceBusReceivedMessage message)
        => string.Equals(
            message.ContentType,
            IngestionContentTypes.BlobReference,
            StringComparison.OrdinalIgnoreCase);
}
