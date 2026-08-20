namespace RateAlerts.Api.Configuration;

/// <summary>Bound from the "Xecd" config section. Replaces reading magic strings off IConfiguration.</summary>
public sealed class XeOptions
{
    public const string SectionName = "Xecd";

    public required string AccountId { get; init; }

    public required string ApiKey { get; init; }

    public string BaseUrl { get; init; } = "https://xecdapi.xe.com/v1/";

    /// <summary>How long a fetched rate is trusted before we ask Xe again. Xe is a metered upstream.</summary>
    public int CacheSeconds { get; init; } = 30;

    /// <summary>
    /// The supplied Xe sandbox key returns a constant 1.2345 for every pair (verified against
    /// USD/JPY, USD/KRW, GBP/CAD, etc. — real markets differ by orders of magnitude). That makes
    /// alert evaluation degenerate, so local development can opt into a fake provider that returns
    /// varied, drifting rates instead. Never true outside Development; see Program.cs.
    /// </summary>
    public bool UseFakeRates { get; init; }
}
