namespace RateAlerts.Api.Models;

/// <summary>
/// A user's rule: notify when Pair crosses Threshold in Direction. Whether it is currently
/// triggered is not stored here - see AlertEvaluator. An alert is a rule, not a rule plus its last
/// known answer; storing the answer means owning its staleness.
/// </summary>
public sealed record Alert(
    Guid Id,
    CurrencyPair Pair,
    decimal Threshold,
    AlertDirection Direction,
    DateTimeOffset CreatedAt);
