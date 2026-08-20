namespace RateAlerts.Api.Models;

/// <summary>The three pairs the rate board shows. Alerts are not limited to this set - see CurrencyPair.</summary>
public static class DisplayedPairs
{
    public static readonly IReadOnlyList<CurrencyPair> All =
    [
        CurrencyPair.Parse("USD/CAD"),
        CurrencyPair.Parse("GBP/USD"),
        CurrencyPair.Parse("EUR/USD"),
    ];
}
