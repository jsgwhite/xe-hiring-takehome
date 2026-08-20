namespace RateAlerts.Api.Models;

/// <summary>Wire contract for the alert endpoints. Direction and Status are lower-case strings to
/// match the README-documented contract ("above"/"below"), not the C# enum member names.</summary>
public sealed record AlertDto(
    Guid Id,
    string Pair,
    decimal Threshold,
    string Direction,
    bool Triggered,
    decimal? CurrentRate,
    DateTimeOffset? AsOf,
    string Status);

/// <summary>Wire contract for POST /api/alerts. Pair/Direction stay strings here (rather than
/// CurrencyPair/AlertDirection) because malformed input needs to become a 400 with a clear message,
/// not a model-binding failure.</summary>
public sealed record CreateAlertRequest(string Pair, decimal Threshold, string Direction);
