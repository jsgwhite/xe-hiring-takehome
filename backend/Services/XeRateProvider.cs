using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using RateAlerts.Api.Configuration;
using RateAlerts.Api.Models;

namespace RateAlerts.Api.Services;

/// <summary>
/// Fetches rates from the Xe Currency Data API's convert_from.json endpoint.
///
/// Two upstream quirks discovered by probing the live API before writing this, both load-bearing:
///
/// 1. An unknown currency code does not error - it comes back HTTP 200 with an empty "to" array.
///    EnsureSuccessStatusCode does not catch this, so we treat an empty/missing entry as "rate
///    unavailable" rather than indexing into the array.
/// 2. A multi-currency "to" list comes back reordered (alphabetical, not request order) - e.g.
///    requesting to=CAD,GBP,EUR returns CAD,EUR,GBP. Results are matched on quotecurrency, never
///    by position.
/// </summary>
public sealed class XeRateProvider : IRateProvider
{
    private readonly HttpClient _httpClient;
    private readonly XeOptions _options;
    private readonly ILogger<XeRateProvider> _logger;

    public XeRateProvider(HttpClient httpClient, IOptions<XeOptions> options, ILogger<XeRateProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyDictionary<CurrencyPair, Rate>> GetRatesAsync(
        IReadOnlyCollection<CurrencyPair> pairs,
        CancellationToken cancellationToken)
    {
        if (pairs.Count == 0)
        {
            return new Dictionary<CurrencyPair, Rate>();
        }

        // Xe's convert_from endpoint takes one base and a comma-separated list of quotes, so pairs
        // sharing a base collapse into a single upstream call instead of one call each.
        var pairsByBase = pairs
            .Distinct()
            .GroupBy(pair => pair.Base)
            .ToList();

        var fetches = pairsByBase.Select(group => FetchForBaseAsync(group.Key, group.ToList(), cancellationToken));
        var results = await Task.WhenAll(fetches);

        var rates = new Dictionary<CurrencyPair, Rate>();
        foreach (var batch in results)
        {
            foreach (var (pair, rate) in batch)
            {
                rates[pair] = rate;
            }
        }

        return rates;
    }

    private async Task<IReadOnlyDictionary<CurrencyPair, Rate>> FetchForBaseAsync(
        string baseCurrency,
        IReadOnlyList<CurrencyPair> pairsForBase,
        CancellationToken cancellationToken)
    {
        var quoteCurrencies = string.Join(',', pairsForBase.Select(pair => pair.Quote));
        var requestUri = $"convert_from.json/?from={baseCurrency}&to={quoteCurrencies}";

        ConvertFromResponse? response;
        try
        {
            response = await _httpClient.GetFromJsonAsync<ConvertFromResponse>(requestUri, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Failed to fetch rates for base currency {BaseCurrency}", baseCurrency);
            return new Dictionary<CurrencyPair, Rate>();
        }

        if (response?.To is null)
        {
            return new Dictionary<CurrencyPair, Rate>();
        }

        var asOf = ParseTimestamp(response.Timestamp);

        // Index by quote currency rather than trusting array order - see class remarks, quirk 2.
        var byQuoteCurrency = response.To
            .Where(quote => quote.QuoteCurrency is not null)
            .ToDictionary(quote => quote.QuoteCurrency!, quote => quote.Mid, StringComparer.OrdinalIgnoreCase);

        var rates = new Dictionary<CurrencyPair, Rate>();
        foreach (var pair in pairsForBase)
        {
            // A quote currency absent from the response (quirk 1: Xe returns 200 with an empty
            // array for an unknown code) is left out of the result, not defaulted to zero.
            if (byQuoteCurrency.TryGetValue(pair.Quote, out var mid))
            {
                rates[pair] = new Rate(pair, Math.Round(mid, 4), asOf);
            }
        }

        return rates;
    }

    private static DateTimeOffset ParseTimestamp(string? timestamp)
        => DateTimeOffset.TryParse(
            timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow;

    private sealed record ConvertFromResponse(
        [property: JsonPropertyName("timestamp")] string? Timestamp,
        [property: JsonPropertyName("to")] IReadOnlyList<ConvertFromQuote>? To);

    private sealed record ConvertFromQuote(
        [property: JsonPropertyName("quotecurrency")] string? QuoteCurrency,
        [property: JsonPropertyName("mid")] decimal Mid);
}
