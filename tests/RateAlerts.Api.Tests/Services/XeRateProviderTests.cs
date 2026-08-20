using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RateAlerts.Api.Configuration;
using RateAlerts.Api.Models;
using RateAlerts.Api.Services;

namespace RateAlerts.Api.Tests.Services;

/// <summary>
/// Covers the two upstream quirks discovered by probing the live Xe API before writing the provider:
/// an unknown currency comes back HTTP 200 with an empty "to" array rather than an error, and a
/// multi-currency response is reordered relative to the request. Both are real bugs the original
/// RatesController would have hit; these tests pin the fix.
/// </summary>
public class XeRateProviderTests
{
    private static XeRateProvider CreateProvider(StubHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://xecdapi.xe.com/v1/") };
        var options = Options.Create(new XeOptions { AccountId = "acct", ApiKey = "key" });
        return new XeRateProvider(httpClient, options, NullLogger<XeRateProvider>.Instance);
    }

    [Fact]
    public async Task Unknown_currency_returns_no_rate_instead_of_throwing()
    {
        // Verified against the real API: from=USD&to=ZZZ -> 200 OK, "to": [].
        var handler = new StubHttpMessageHandler(
            """{"from":"USD","timestamp":"2026-08-19T23:53:00Z","to":[]}""");
        var provider = CreateProvider(handler);

        var rates = await provider.GetRatesAsync([CurrencyPair.Parse("USD/ZZZ")], CancellationToken.None);

        Assert.Empty(rates);
    }

    [Fact]
    public async Task Matches_results_by_quote_currency_not_array_position()
    {
        // Verified against the real API: requesting to=CAD,GBP,EUR returns them back as CAD,EUR,GBP.
        var handler = new StubHttpMessageHandler("""
            {
              "from": "USD",
              "timestamp": "2026-08-19T23:53:00Z",
              "to": [
                { "quotecurrency": "CAD", "mid": 1.3650 },
                { "quotecurrency": "EUR", "mid": 0.9200 },
                { "quotecurrency": "GBP", "mid": 0.7500 }
              ]
            }
            """);
        var provider = CreateProvider(handler);

        var pairs = new[]
        {
            CurrencyPair.Parse("USD/CAD"),
            CurrencyPair.Parse("USD/GBP"),
            CurrencyPair.Parse("USD/EUR"),
        };

        var rates = await provider.GetRatesAsync(pairs, CancellationToken.None);

        Assert.Equal(1.3650m, rates[CurrencyPair.Parse("USD/CAD")].Mid);
        Assert.Equal(0.7500m, rates[CurrencyPair.Parse("USD/GBP")].Mid);
        Assert.Equal(0.9200m, rates[CurrencyPair.Parse("USD/EUR")].Mid);
    }

    [Fact]
    public async Task Groups_pairs_sharing_a_base_currency_into_a_single_request()
    {
        var handler = new StubHttpMessageHandler("""
            {"from":"USD","timestamp":"2026-08-19T23:53:00Z",
             "to":[{"quotecurrency":"CAD","mid":1.3650},{"quotecurrency":"GBP","mid":0.7500}]}
            """);
        var provider = CreateProvider(handler);

        var pairs = new[] { CurrencyPair.Parse("USD/CAD"), CurrencyPair.Parse("USD/GBP") };
        await provider.GetRatesAsync(pairs, CancellationToken.None);

        Assert.Equal(1, handler.RequestCount);
        Assert.Contains("to=CAD,GBP", handler.LastRequestUri);
    }

    [Fact]
    public async Task Different_base_currencies_produce_concurrent_separate_requests()
    {
        var handler = new StubHttpMessageHandler("""
            {"from":"USD","timestamp":"2026-08-19T23:53:00Z","to":[{"quotecurrency":"CAD","mid":1.3650}]}
            """);
        var provider = CreateProvider(handler);

        var pairs = new[] { CurrencyPair.Parse("USD/CAD"), CurrencyPair.Parse("GBP/CAD") };
        await provider.GetRatesAsync(pairs, CancellationToken.None);

        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task Upstream_failure_returns_no_rates_rather_than_throwing()
    {
        var handler = new StubHttpMessageHandler(statusCode: HttpStatusCode.Unauthorized);
        var provider = CreateProvider(handler);

        var rates = await provider.GetRatesAsync([CurrencyPair.Parse("USD/CAD")], CancellationToken.None);

        Assert.Empty(rates);
    }

    [Fact]
    public async Task No_pairs_requested_makes_no_http_call()
    {
        var handler = new StubHttpMessageHandler("""{"to":[]}""");
        var provider = CreateProvider(handler);

        var rates = await provider.GetRatesAsync([], CancellationToken.None);

        Assert.Empty(rates);
        Assert.Equal(0, handler.RequestCount);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly string? _responseBody;
        private readonly HttpStatusCode _statusCode;

        public int RequestCount { get; private set; }
        public string? LastRequestUri { get; private set; }

        public StubHttpMessageHandler(string? responseBody = null, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responseBody = responseBody;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequestUri = request.RequestUri?.ToString();

            var response = new HttpResponseMessage(_statusCode);
            if (_responseBody is not null)
            {
                response.Content = new StringContent(_responseBody, Encoding.UTF8, "application/json");
            }

            return Task.FromResult(response);
        }
    }
}
