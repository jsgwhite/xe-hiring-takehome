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
///
/// IMPORTANT - why this class knows which currency codes are real:
///
/// An earlier version of this provider invented a rate for *any* well-formed pair, on the reasoning
/// that a demo double only needs to supply plausible numbers. That was wrong, and it hid a live bug
/// for a while, so the reasoning is worth recording rather than just the fix.
///
/// AlertsController rejects a POST whose pair evaluates to RateUnavailable, because Xe answers an
/// unknown currency code with HTTP 200 and an empty "to" array rather than an error - so "no rate
/// came back" is the *only* signal that a code is bogus. That guard is covered by a controller test
/// using a stub provider, and it works correctly against the real Xe API.
///
/// But with UseFakeRates on - the default in Development, i.e. the configuration a reviewer running
/// `dotnet run` actually gets - the old fake returned a synthesised rate for USD/ZZZ too. No rate was
/// ever unavailable, so the RateUnavailable branch never executed, and POST /api/alerts happily
/// created an alert on a currency that does not exist, reporting a completely fabricated rate for it.
/// Green test suite, correct production behaviour, broken demo.
///
/// The lesson generalises past this one bug: a test double that reproduces only the upstream's happy
/// path silently disables every code path that exists to handle the upstream's failures. This fake
/// models the sandbox's *constant-rate* quirk (its original purpose) and now also its
/// *unknown-code* quirk, so the two behaviours the system genuinely depends on are both present
/// locally. Anything added here later should ask the same question: does the real API do this?
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

    /// <summary>
    /// Currency codes this fake will quote. Not the full ISO 4217 set - Xe publishes ~170 at
    /// /v1/currencies.json and fetching that list at startup is the production-shaped solution (see
    /// NOTES.md, "What's next"). This subset is enough for the fake to do its real job: answer
    /// "is this a currency that exists?" the same way the upstream would, so an unknown code
    /// produces no rate here exactly as it produces an empty "to" array there.
    /// </summary>
    private static readonly IReadOnlySet<string> KnownCurrencies = new HashSet<string>(StringComparer.Ordinal)
    {
        "AED", "ARS", "AUD", "BGN", "BRL", "CAD", "CHF", "CLP", "CNY", "COP",
        "CZK", "DKK", "EGP", "EUR", "GBP", "HKD", "HUF", "IDR", "ILS", "INR",
        "ISK", "JPY", "KES", "KRW", "MAD", "MXN", "MYR", "NGN", "NOK", "NZD",
        "PEN", "PHP", "PKR", "PLN", "RON", "RUB", "SAR", "SEK", "SGD", "THB",
        "TRY", "TWD", "UAH", "USD", "VND", "ZAR",
    };

    public Task<IReadOnlyDictionary<CurrencyPair, Rate>> GetRatesAsync(
        IReadOnlyCollection<CurrencyPair> pairs,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        // A pair naming a currency that does not exist is omitted from the result, mirroring the way
        // XeRateProvider omits a quote currency the upstream left out of its "to" array. Callers read
        // "absent from the dictionary" as RateUnavailable - see AlertEvaluator.
        var rates = pairs
            .Distinct()
            .Where(IsKnownPair)
            .ToDictionary(pair => pair, pair => new Rate(pair, RateFor(pair, now), now));

        return Task.FromResult<IReadOnlyDictionary<CurrencyPair, Rate>>(rates);
    }

    private static bool IsKnownPair(CurrencyPair pair)
        => KnownCurrencies.Contains(pair.Base) && KnownCurrencies.Contains(pair.Quote);

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

    /// <summary>Deterministic fallback for a real pair with no entry in <see cref="BaseRates"/> - e.g.
    /// EUR/SEK - so an arbitrary but genuine pair still gets a stable, sane-looking rate instead of a
    /// hardcoded 1.0. Only reached for pairs that passed <see cref="IsKnownPair"/>.</summary>
    private static decimal SyntheticBaseRate(CurrencyPair pair)
    {
        var seed = Math.Abs(pair.GetHashCode());
        return 0.5m + (seed % 200) / 100m; // spread across roughly 0.5 - 2.5
    }
}
