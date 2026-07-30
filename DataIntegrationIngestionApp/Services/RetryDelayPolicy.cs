using System;
using DataIntegrationIngestionApp.Options;
using Microsoft.Extensions.Options;

namespace DataIntegrationIngestionApp.Services;

/// <summary>
/// Capped exponential backoff based on the message DeliveryCount:
/// delay = min(BaseDelay * 2^(deliveryCount - 1), MaxDelay).
/// </summary>
public sealed class RetryDelayPolicy : IRetryDelayPolicy
{
    private readonly BackoffOptions _backoff;

    public RetryDelayPolicy(IOptions<IngestionOptions> options)
    {
        _backoff = options.Value.Backoff;
    }

    public TimeSpan GetDelay(int deliveryCount)
    {
        var attempt = Math.Max(1, deliveryCount);

        // Guard against overflow for large delivery counts.
        var exponent = Math.Min(attempt - 1, 30);
        var factor = Math.Pow(2, exponent);

        var seconds = _backoff.BaseDelaySeconds * factor;
        var cappedSeconds = Math.Min(seconds, _backoff.MaxDelaySeconds);

        return TimeSpan.FromSeconds(cappedSeconds);
    }
}
