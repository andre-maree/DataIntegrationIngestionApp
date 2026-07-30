using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;

namespace DataIntegrationIngestionApp.Services;

/// <summary>
/// Reads the raw JSON payload either directly from the message body (inline)
/// or from a referenced blob when the payload is too large to fit in a message.
/// </summary>
public sealed class PayloadReader : IPayloadReader
{
    private readonly BlobServiceClient? _blobServiceClient;
    private readonly ILogger<PayloadReader> _logger;

    public PayloadReader(ILogger<PayloadReader> logger, BlobServiceClient? blobServiceClient = null)
    {
        _logger = logger;
        _blobServiceClient = blobServiceClient;
    }

    public async Task<string> ReadPayloadAsync(
        ServiceBusReceivedMessage message,
        bool isBlobReference,
        CancellationToken cancellationToken = default)
    {
        if (!isBlobReference)
        {
            return message.Body.ToString();
        }

        var reference = ParseBlobReference(message.Body.ToString());

        if (_blobServiceClient is null)
        {
            throw new InvalidOperationException(
                "Message is a blob reference but no BlobServiceClient is configured. " +
                "Set the blob storage connection string in configuration.");
        }

        _logger.LogInformation(
            "Reading payload from blob {Container}/{Blob}.",
            reference.Container,
            reference.Blob);

        var containerClient = _blobServiceClient.GetBlobContainerClient(reference.Container);
        var blobClient = containerClient.GetBlobClient(reference.Blob);

        var download = await blobClient.DownloadContentAsync(cancellationToken).ConfigureAwait(false);
        return download.Value.Content.ToString();
    }

    private static BlobReference ParseBlobReference(string body)
    {
        BlobReference? reference;
        try
        {
            reference = JsonSerializer.Deserialize<BlobReference>(
                body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                "Message content-type indicates a blob reference but the body could not be parsed.", ex);
        }

        if (reference is null ||
            string.IsNullOrWhiteSpace(reference.Container) ||
            string.IsNullOrWhiteSpace(reference.Blob))
        {
            throw new InvalidOperationException(
                "Blob reference message must contain non-empty 'container' and 'blob' properties.");
        }

        return reference;
    }

    private sealed class BlobReference
    {
        public string Container { get; set; } = string.Empty;

        public string Blob { get; set; } = string.Empty;
    }
}
