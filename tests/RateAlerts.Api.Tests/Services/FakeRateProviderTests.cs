using RateAlerts.Api.Models;
using RateAlerts.Api.Services;

namespace RateAlerts.Api.Tests.Services;

/// <summary>
/// These read as tests of a test double, which is unusual and deserves a word.
///
/// FakeRateProvider is the provider a reviewer running `dotnet run` actually gets - UseFakeRates is
/// on in appsettings.Development.json - so its behaviour *is* the behaviour of the demo. It earns
/// tests for the same reason production code does.
///
/// Specifically, the unknown-currency tests below pin down a bug this class previously had: it
/// invented a rate for any well-formed pair, including USD/ZZZ, which meant no rate was ever
/// unavailable and AlertsController's RateUnavailable guard never ran locally. The controller test
/// for that guard passed the whole time - it uses its own stub - so nothing in the suite noticed the
/// demo was accepting alerts on currencies that do not exist. Hence a test at this level.
/// </summary>
public class FakeRateProviderTests
{
    private static readonly FakeRateProvider Provider = new();

    private static Task<IReadOnlyDictionary<CurrencyPair, Rate>> GetAsync(params string[] pairs)
        => Provider.GetRatesAsync(pairs.Select(CurrencyPair.Parse).ToList(), CancellationToken.None);

    [Fact]
    public async Task Returns_a_rate_for_a_pair_it_has_a_configured_base_rate_for()
    {
        var rates = await GetAsync("USD/CAD");

        var rate = Assert.Single(rates).Value;
        Assert.Equal(CurrencyPair.Parse("USD/CAD"), rate.Pair);
        Assert.InRange(rate.Mid, 1.30m, 1.45m); // 1.3650 base, +/-1% drift
    }

    [Fact]
    public async Task Returns_a_rate_for_a_real_pair_with_no_configured_base_rate()
    {
        // The point of the synthetic fallback: arbitrary-but-genuine pairs still demo correctly.
        var rates = await GetAsync("EUR/SEK");

        var rate = Assert.Single(rates).Value;
        Assert.True(rate.Mid > 0);
    }

    [Theory]
    [InlineData("USD/ZZZ")] // unknown quote
    [InlineData("ZZZ/USD")] // unknown base
    [InlineData("ZZZ/QQQ")] // both unknown
    public async Task Omits_a_pair_naming_a_currency_that_does_not_exist(string pair)
    {
        // Mirrors the real Xe API, which answers an unknown code with HTTP 200 and an empty "to"
        // array - XeRateProvider omits it from the result, and so must this. Callers read absence as
        // RateUnavailable, which is what makes AlertsController reject the alert.
        var rates = await GetAsync(pair);

        Assert.Empty(rates);
    }

    [Fact]
    public async Task Omits_only_the_unknown_pair_from_a_batch_and_still_returns_the_rest()
    {
        var rates = await GetAsync("USD/CAD", "USD/ZZZ", "GBP/USD");

        Assert.Equal(2, rates.Count);
        Assert.True(rates.ContainsKey(CurrencyPair.Parse("USD/CAD")));
        Assert.True(rates.ContainsKey(CurrencyPair.Parse("GBP/USD")));
        Assert.False(rates.ContainsKey(CurrencyPair.Parse("USD/ZZZ")));
    }

    [Fact]
    public async Task Two_calls_moments_apart_return_closely_related_rates()
    {
        // The drift is deterministic and slow on purpose - a demo where the rate jumps randomly
        // between two refreshes makes triggered state look arbitrary rather than explicable.
        var first = await GetAsync("USD/CAD");
        var second = await GetAsync("USD/CAD");

        var firstMid = first.Values.Single().Mid;
        var secondMid = second.Values.Single().Mid;

        Assert.True(Math.Abs(firstMid - secondMid) < 0.01m);
    }

    [Fact]
    public async Task Requesting_no_pairs_returns_no_rates()
    {
        var rates = await Provider.GetRatesAsync([], CancellationToken.None);

        Assert.Empty(rates);
    }
}
