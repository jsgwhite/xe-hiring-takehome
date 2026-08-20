using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using RateAlerts.Api.Configuration;
using RateAlerts.Api.Models;
using RateAlerts.Api.Services;

namespace RateAlerts.Api.Tests.Services;

public class CachingRateProviderTests
{
    private sealed class CountingRateProvider : IRateProvider
    {
        public int CallCount { get; private set; }
        public IReadOnlyCollection<CurrencyPair>? LastRequestedPairs { get; private set; }

        public Task<IReadOnlyDictionary<CurrencyPair, Rate>> GetRatesAsync(
            IReadOnlyCollection<CurrencyPair> pairs, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequestedPairs = pairs;

            var now = DateTimeOffset.UtcNow;
            IReadOnlyDictionary<CurrencyPair, Rate> result =
                pairs.ToDictionary(pair => pair, pair => new Rate(pair, 1.5m, now));
            return Task.FromResult(result);
        }
    }

    private static CachingRateProvider CreateProvider(CountingRateProvider inner, int cacheSeconds = 30)
    {
        var options = Options.Create(new XeOptions { AccountId = "a", ApiKey = "k", CacheSeconds = cacheSeconds });
        return new CachingRateProvider(inner, new MemoryCache(new MemoryCacheOptions()), options);
    }

    [Fact]
    public async Task Second_request_for_the_same_pair_does_not_hit_the_inner_provider()
    {
        var inner = new CountingRateProvider();
        var provider = CreateProvider(inner);
        var pair = CurrencyPair.Parse("USD/CAD");

        await provider.GetRatesAsync([pair], CancellationToken.None);
        await provider.GetRatesAsync([pair], CancellationToken.None);

        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task Only_uncached_pairs_are_forwarded_to_the_inner_provider()
    {
        var inner = new CountingRateProvider();
        var provider = CreateProvider(inner);

        await provider.GetRatesAsync([CurrencyPair.Parse("USD/CAD")], CancellationToken.None);
        await provider.GetRatesAsync(
            [CurrencyPair.Parse("USD/CAD"), CurrencyPair.Parse("GBP/USD")], CancellationToken.None);

        Assert.Equal(2, inner.CallCount);
        Assert.Equal([CurrencyPair.Parse("GBP/USD")], inner.LastRequestedPairs);
    }

    [Fact]
    public async Task Returns_rates_for_every_requested_pair_whether_cached_or_not()
    {
        var inner = new CountingRateProvider();
        var provider = CreateProvider(inner);

        await provider.GetRatesAsync([CurrencyPair.Parse("USD/CAD")], CancellationToken.None);
        var rates = await provider.GetRatesAsync(
            [CurrencyPair.Parse("USD/CAD"), CurrencyPair.Parse("GBP/USD")], CancellationToken.None);

        Assert.Equal(2, rates.Count);
    }

    [Fact]
    public async Task Expired_entry_is_refetched()
    {
        var inner = new CountingRateProvider();
        var provider = CreateProvider(inner, cacheSeconds: 1);
        var pair = CurrencyPair.Parse("USD/CAD");

        await provider.GetRatesAsync([pair], CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(1.2));
        await provider.GetRatesAsync([pair], CancellationToken.None);

        Assert.Equal(2, inner.CallCount);
    }
}
