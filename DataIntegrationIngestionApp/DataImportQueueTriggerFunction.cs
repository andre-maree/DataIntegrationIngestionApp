using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using DataIntegrationIngestionApp.Messaging;
using DataIntegrationIngestionApp.Options;
using DataIntegrationIngestionApp.Services;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataIntegrationIngestionApp;

/// <summary>
/// Session-enabled Service Bus ingestion function. Each session maps to a single SQL
/// table (SessionId = schema-qualified table name), guaranteeing strict per-table FIFO.
/// A failing batch is retried in place (Layer 2 delay + abandon → in-order redelivery)
/// until it succeeds or reaches MaxDeliveryCount, after which it is dead-lettered.
/// </summary>
public class DataImportQueueTriggerFunction
{
    private readonly IPayloadReader _payloadReader;
    private readonly IRecordDeserializer _recordDeserializer;
    private readonly ISqlBulkInserter _bulkInserter;
    private readonly IRetryDelayPolicy _retryDelayPolicy;
    private readonly IngestionOptions _options;
    private readonly TelemetryClient _telemetryClient;
    private readonly ILogger<DataImportQueueTriggerFunction> _logger;

    public DataImportQueueTriggerFunction(
        IPayloadReader payloadReader,
        IRecordDeserializer recordDeserializer,
        ISqlBulkInserter bulkInserter,
        IRetryDelayPolicy retryDelayPolicy,
        IOptions<IngestionOptions> options,
        TelemetryClient telemetryClient,
        ILogger<DataImportQueueTriggerFunction> logger)
    {
        _payloadReader = payloadReader;
        _recordDeserializer = recordDeserializer;
        _bulkInserter = bulkInserter;
        _retryDelayPolicy = retryDelayPolicy;
        _options = options.Value;
        _telemetryClient = telemetryClient;
        _logger = logger;
    }

    [Function("DataImportQueueTrigger")]
    public async Task Run(
        [ServiceBusTrigger("ingestion-queue", Connection = "ServiceBusConnection", IsSessionsEnabled = true)]
        ServiceBusReceivedMessage message,
        ServiceBusMessageActions messageActions,
        CancellationToken cancellationToken)
    {
        // SessionId is the schema-qualified destination table (e.g. "dbo.Contacts").
        var destinationTable = message.SessionId;

        if (string.IsNullOrWhiteSpace(destinationTable))
        {
            _logger.LogError("Message {MessageId} has no SessionId; cannot resolve target table.", message.MessageId);
            await messageActions.DeadLetterMessageAsync(
                message,
                deadLetterReason: "MissingSessionId",
                deadLetterErrorDescription: "SessionId (target table) is required for ingestion.",
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }

        var properties = new Dictionary<string, string>
        {
            ["Table"] = destinationTable,
            ["SessionId"] = destinationTable,
            ["MessageId"] = message.MessageId,
            ["DeliveryCount"] = message.DeliveryCount.ToString(),
        };

        try
        {
            var isBlobReference = MessageClassifier.IsBlobReference(message);
            var json = await _payloadReader
                .ReadPayloadAsync(message, isBlobReference, cancellationToken)
                .ConfigureAwait(false);

            var records = _recordDeserializer.Deserialize(json);

            await _bulkInserter
                .BulkInsertAsync(destinationTable, records, cancellationToken)
                .ConfigureAwait(false);

            await messageActions.CompleteMessageAsync(message, cancellationToken).ConfigureAwait(false);

            properties["RecordCount"] = records.Count.ToString();
            _telemetryClient.TrackEvent("BatchIngested", properties);
            _logger.LogInformation(
                "Ingested {Count} record(s) into {Table} (session {SessionId}).",
                records.Count,
                destinationTable,
                destinationTable);
        }
        catch (Exception ex)
        {
            await HandleFailureAsync(message, messageActions, destinationTable, properties, ex, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task HandleFailureAsync(
        ServiceBusReceivedMessage message,
        ServiceBusMessageActions messageActions,
        string destinationTable,
        IDictionary<string, string> properties,
        Exception ex,
        CancellationToken cancellationToken)
    {
        _telemetryClient.TrackException(ex, properties);
        _telemetryClient.TrackMetric("SessionRetryDepth", message.DeliveryCount, properties);

        // DeliveryCount is 1-based for the current attempt. Once it reaches the configured
        // maximum, stop retrying and dead-letter the batch (strict FIFO safety valve).
        if (message.DeliveryCount >= _options.MaxDeliveryCount)
        {
            _logger.LogError(
                ex,
                "Batch for {Table} exhausted {MaxDeliveryCount} deliveries; dead-lettering (session {SessionId}).",
                destinationTable,
                _options.MaxDeliveryCount,
                destinationTable);

            _telemetryClient.TrackEvent("BatchDeadLettered", properties);

            await messageActions.DeadLetterMessageAsync(
                message,
                deadLetterReason: "MaxDeliveryCountExceeded",
                deadLetterErrorDescription: Truncate(ex.Message, 4096),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }

        // Layer 2: bounded in-process delay before abandoning, throttling redeliveries.
        var delay = _retryDelayPolicy.GetDelay(message.DeliveryCount);
        _logger.LogWarning(
            ex,
            "Batch for {Table} failed on delivery {DeliveryCount}; waiting {Delay} then abandoning for in-order retry (session {SessionId}).",
            destinationTable,
            message.DeliveryCount,
            delay,
            destinationTable);

        _telemetryClient.TrackEvent("BatchRetryScheduled", properties);

        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        // Layer 3: abandon → Service Bus redelivers the same message first (FIFO preserved).
        await messageActions.AbandonMessageAsync(message, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}