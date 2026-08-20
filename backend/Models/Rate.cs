namespace RateAlerts.Api.Models;

/// <summary>A quoted mid-market rate for a currency pair at a point in time.</summary>
public sealed record Rate(CurrencyPair Pair, decimal Mid, DateTimeOffset AsOf);
