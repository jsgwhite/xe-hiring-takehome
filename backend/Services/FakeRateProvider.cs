using RateAlerts.Api.Models;

namespace RateAlerts.Api.Services;

/// <summary>
/// Development-only stand-in for <see cref="XeRateProvider"/>. The Xe sandbox key wired into this
/// project returns a constant 1.2345 for every pair - verified against USD/JPY, USD/KRW, GBP/CAD and
/// others, where real rates differ by orders of magnitude. Against that, an alert's triggered state
/// can never be demonstrated: every threshold above 1.2345 never fires, every threshold below always
/// does, identically for every pair.
///
/// This provider gives each pair a distinct, plausible base rate and a slow sinusoidal drift over
/// time, so alerts can actually be seen flipping between triggered and not. Enabled only via
/// Xecd:UseFakeRates, and Program.cs refuses to honour that flag outside Development.
/// </summary>
public sealed class FakeRateProvider : IRateProvider
{
    // A handful of plausible base rates so the demo isn't every pair converging on 1.0.
    private static readonly IReadOnlyDictionary<string, decimal> BaseRates = new Dictionary<string, decimal>
    {
        ["USD/CAD"] = 1.3650m,
        ["GBP/USD"] = 1.2710m,
        ["EUR/USD"] = 1.0830m,
        ["GBP/CAD"] = 1.7350m,
        ["USD/JPY"] = 149.80m,
        ["AUD/JPY"] = 97.40m,
    };

    public Task<IReadOnlyDictionary<CurrencyPair, Rate>> GetRatesAsync(
        IReadOnlyCollection<CurrencyPair> pairs,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var rates = pairs
            .Distinct()
            .ToDictionary(pair => pair, pair => new Rate(pair, RateFor(pair, now), now));

        return Task.FromResult<IReadOnlyDictionary<CurrencyPair, Rate>>(rates);
    }

    private static decimal RateFor(CurrencyPair pair, DateTimeOffset now)
    {
        var baseRate = BaseRates.TryGetValue(pair.ToString(), out var known) ? known : SyntheticBaseRate(pair);

        // A slow, deterministic drift (period-minutes scale) rather than random noise, so two calls
        // moments apart in a demo return visibly-related values instead of jumping around.
        var phase = pair.GetHashCode() % 360;
        var minutesElapsed = now.ToUnixTimeSeconds() / 60.0;
        var driftFraction = 0.01m * (decimal)Math.Sin((minutesElapsed + phase) / 15.0);

        return Math.Round(baseRate * (1 + driftFraction), 4);
    }

    /// <summary>Deterministic fallback for a pair with no entry in <see cref="BaseRates"/>, so an
    /// arbitrary pair still gets a stable, sane-looking rate instead of a hardcoded 1.0.</summary>
    private static decimal SyntheticBaseRate(CurrencyPair pair)
    {
        var seed = Math.Abs(pair.GetHashCode());
        return 0.5m + (seed % 200) / 100m; // spread across roughly 0.5 - 2.5
    }
}
