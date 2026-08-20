using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using RateAlerts.Api.Configuration;
using RateAlerts.Api.Models;

namespace RateAlerts.Api.Services;

/// <summary>
/// Decorates an <see cref="IRateProvider"/> with a short-lived cache. Xe is a metered upstream and
/// both the rate board and every alert evaluation ask for rates - without this, listing N alerts
/// costs N (batched) round-trips instead of sharing one.
/// </summary>
public sealed class CachingRateProvider : IRateProvider
{
    private readonly IRateProvider _inner;
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _ttl;

    public CachingRateProvider(IRateProvider inner, IMemoryCache cache, IOptions<XeOptions> options)
    {
        _inner = inner;
        _cache = cache;
        _ttl = TimeSpan.FromSeconds(options.Value.CacheSeconds);
    }

    public async Task<IReadOnlyDictionary<CurrencyPair, Rate>> GetRatesAsync(
        IReadOnlyCollection<CurrencyPair> pairs,
        CancellationToken cancellationToken)
    {
        var distinctPairs = pairs.Distinct().ToList();
        var result = new Dictionary<CurrencyPair, Rate>();
        var uncached = new List<CurrencyPair>();

        foreach (var pair in distinctPairs)
        {
            if (_cache.TryGetValue(CacheKey(pair), out Rate? cached) && cached is not null)
            {
                result[pair] = cached;
            }
            else
            {
                uncached.Add(pair);
            }
        }

        if (uncached.Count > 0)
        {
            var fetched = await _inner.GetRatesAsync(uncached, cancellationToken);
            foreach (var (pair, rate) in fetched)
            {
                _cache.Set(CacheKey(pair), rate, _ttl);
                result[pair] = rate;
            }
        }

        return result;
    }

    private static string CacheKey(CurrencyPair pair) => $"rate:{pair}";
}
