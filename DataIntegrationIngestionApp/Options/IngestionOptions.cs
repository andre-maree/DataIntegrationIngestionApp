namespace DataIntegrationIngestionApp.Options;

/// <summary>
/// Configurable ingestion settings bound from configuration section <see cref="SectionName"/>.
/// </summary>
public sealed class IngestionOptions
{
    public const string SectionName = "Ingestion";

    /// <summary>
    /// Maximum number of Service Bus deliveries for a batch before it is dead-lettered.
    /// Kept in sync with the queue's MaxDeliveryCount. Default 10.
    /// </summary>
    public int MaxDeliveryCount { get; set; } = 10;

    /// <summary>
    /// SQL Server connection string used for the bulk insert. Provided later via configuration.
    /// </summary>
    public string? SqlConnectionString { get; set; }

    /// <summary>
    /// Blob storage connection string used to read large payloads referenced by a message.
    /// </summary>
    public string? BlobStorageConnectionString { get; set; }

    /// <summary>
    /// Layer 1 — Microsoft.Data.SqlClient built-in transient retry settings.
    /// </summary>
    public SqlTransientRetryOptions SqlRetry { get; set; } = new();

    /// <summary>
    /// Layer 2 — bounded in-process delayed retry (throttle) before abandoning the message.
    /// </summary>
    public BackoffOptions Backoff { get; set; } = new();
}

/// <summary>
/// Layer 1 transient retry options for <c>SqlRetryLogicOption</c>.
/// </summary>
public sealed class SqlTransientRetryOptions
{
    /// <summary>Number of in-invocation transient retries (seconds-level).</summary>
    public int NumberOfTries { get; set; } = 5;

    /// <summary>Delta back-off between attempts, in seconds.</summary>
    public int DeltaTimeSeconds { get; set; } = 2;

    /// <summary>Maximum interval between attempts, in seconds.</summary>
    public int MaxTimeIntervalSeconds { get; set; } = 20;
}

/// <summary>
/// Layer 2 capped exponential backoff options derived from DeliveryCount.
/// </summary>
public sealed class BackoffOptions
{
    /// <summary>Base delay in seconds for the first delivery.</summary>
    public int BaseDelaySeconds { get; set; } = 5;

    /// <summary>Maximum delay cap in seconds (e.g. 300 = 5 minutes).</summary>
    public int MaxDelaySeconds { get; set; } = 300;
}
