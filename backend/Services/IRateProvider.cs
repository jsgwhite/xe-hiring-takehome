using RateAlerts.Api.Models;

namespace RateAlerts.Api.Services;

/// <summary>
/// Fetches current mid-market rates for a set of currency pairs. Batch-shaped on purpose: both the
/// rate board and alert evaluation want several pairs at once, and a batched interface is what lets
/// an implementation group pairs sharing a base currency into a single upstream call.
/// </summary>
public interface IRateProvider
{
    /// <summary>
    /// Fetches rates for the requested pairs. A pair missing from the result means its rate could
    /// not be obtained (upstream failure, unknown currency) — callers must treat that as "unknown",
    /// not "zero".
    /// </summary>
    Task<IReadOnlyDictionary<CurrencyPair, Rate>> GetRatesAsync(
        IReadOnlyCollection<CurrencyPair> pairs,
        CancellationToken cancellationToken);
}
