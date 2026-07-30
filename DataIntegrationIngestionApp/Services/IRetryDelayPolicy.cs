using System;

namespace DataIntegrationIngestionApp.Services;

/// <summary>
/// Computes the Layer 2 in-process delay applied before abandoning a failed message,
/// throttling redeliveries so a repeatedly failing batch does not hot-loop the session.
/// </summary>
public interface IRetryDelayPolicy
{
    /// <summary>
    /// Returns the capped exponential backoff delay for the given Service Bus delivery count.
    /// </summary>
    /// <param name="deliveryCount">The current message DeliveryCount (1-based).</param>
    TimeSpan GetDelay(int deliveryCount);
}
