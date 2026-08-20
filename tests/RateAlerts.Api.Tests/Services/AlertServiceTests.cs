using Microsoft.Extensions.Logging.Abstractions;
using RateAlerts.Api.Models;
using RateAlerts.Api.Services;

namespace RateAlerts.Api.Tests.Services;

public class AlertServiceTests
{
    private sealed class FakeAlertStore : IAlertStore
    {
        private readonly List<Alert> _alerts = [];

        public IReadOnlyCollection<Alert> GetAll() => _alerts;

        public Alert Add(Alert alert)
        {
            _alerts.Add(alert);
            return alert;
        }

        public bool Remove(Guid id) => _alerts.RemoveAll(a => a.Id == id) > 0;
    }

    private sealed class RecordingRateProvider : IRateProvider
    {
        private readonly IReadOnlyDictionary<CurrencyPair, decimal> _knownRates;
        private readonly Exception? _throws;

        public int CallCount { get; private set; }
        public IReadOnlyCollection<CurrencyPair>? LastRequestedPairs { get; private set; }

        public RecordingRateProvider(IReadOnlyDictionary<CurrencyPair, decimal> knownRates)
        {
            _knownRates = knownRates;
        }

        public RecordingRateProvider(Exception throws)
        {
            _knownRates = new Dictionary<CurrencyPair, decimal>();
            _throws = throws;
        }

        public Task<IReadOnlyDictionary<CurrencyPair, Rate>> GetRatesAsync(
            IReadOnlyCollection<CurrencyPair> pairs, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequestedPairs = pairs;

            if (_throws is not null)
            {
                throw _throws;
            }

            var now = DateTimeOffset.UtcNow;
            IReadOnlyDictionary<CurrencyPair, Rate> result = pairs
                .Where(_knownRates.ContainsKey)
                .ToDictionary(pair => pair, pair => new Rate(pair, _knownRates[pair], now));
            return Task.FromResult(result);
        }
    }

    private static Alert MakeAlert(string pair, decimal threshold, AlertDirection direction) =>
        new(Guid.NewGuid(), CurrencyPair.Parse(pair), threshold, direction, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Empty_store_returns_empty_without_calling_the_rate_provider()
    {
        var store = new FakeAlertStore();
        var rateProvider = new RecordingRateProvider(new Dictionary<CurrencyPair, decimal>());
        var service = new AlertService(store, rateProvider, NullLogger<AlertService>.Instance);

        var result = await service.GetAllWithEvaluationsAsync(CancellationToken.None);

        Assert.Empty(result);
        Assert.Equal(0, rateProvider.CallCount);
    }

    [Fact]
    public async Task Requests_each_distinct_pair_exactly_once_regardless_of_how_many_alerts_share_it()
    {
        var store = new FakeAlertStore();
        store.Add(MakeAlert("GBP/CAD", 1.80m, AlertDirection.Above));
        store.Add(MakeAlert("GBP/CAD", 1.90m, AlertDirection.Below));
        store.Add(MakeAlert("USD/CAD", 1.30m, AlertDirection.Above));

        var rateProvider = new RecordingRateProvider(new Dictionary<CurrencyPair, decimal>
        {
            [CurrencyPair.Parse("GBP/CAD")] = 1.85m,
            [CurrencyPair.Parse("USD/CAD")] = 1.35m,
        });
        var service = new AlertService(store, rateProvider, NullLogger<AlertService>.Instance);

        var result = await service.GetAllWithEvaluationsAsync(CancellationToken.None);

        Assert.Equal(1, rateProvider.CallCount);
        Assert.Equal(2, rateProvider.LastRequestedPairs!.Count);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task Evaluates_each_alert_against_the_fetched_rate()
    {
        var store = new FakeAlertStore();
        var alert = store.Add(MakeAlert("GBP/CAD", 1.80m, AlertDirection.Above));

        var rateProvider = new RecordingRateProvider(new Dictionary<CurrencyPair, decimal>
        {
            [CurrencyPair.Parse("GBP/CAD")] = 1.85m,
        });
        var service = new AlertService(store, rateProvider, NullLogger<AlertService>.Instance);

        var result = await service.GetAllWithEvaluationsAsync(CancellationToken.None);

        var evaluated = Assert.Single(result);
        Assert.Equal(alert, evaluated.Alert);
        Assert.True(evaluated.Evaluation.Triggered);
        Assert.Equal(1.85m, evaluated.Evaluation.CurrentRate);
    }

    [Fact]
    public async Task Pair_missing_from_the_provider_response_evaluates_as_unavailable()
    {
        var store = new FakeAlertStore();
        store.Add(MakeAlert("GBP/CAD", 1.80m, AlertDirection.Above));

        // Provider knows nothing, mirroring the real XeRateProvider's behaviour when Xe returns an
        // empty "to" array for a pair it can't quote.
        var rateProvider = new RecordingRateProvider(new Dictionary<CurrencyPair, decimal>());
        var service = new AlertService(store, rateProvider, NullLogger<AlertService>.Instance);

        var result = await service.GetAllWithEvaluationsAsync(CancellationToken.None);

        var evaluated = Assert.Single(result);
        Assert.Equal(EvaluationStatus.RateUnavailable, evaluated.Evaluation.Status);
        Assert.False(evaluated.Evaluation.Triggered);
    }

    [Fact]
    public async Task Rate_provider_throwing_still_lists_alerts_as_unavailable_rather_than_propagating()
    {
        var store = new FakeAlertStore();
        store.Add(MakeAlert("GBP/CAD", 1.80m, AlertDirection.Above));

        var rateProvider = new RecordingRateProvider(new HttpRequestException("upstream is down"));
        var service = new AlertService(store, rateProvider, NullLogger<AlertService>.Instance);

        var result = await service.GetAllWithEvaluationsAsync(CancellationToken.None);

        var evaluated = Assert.Single(result);
        Assert.Equal(EvaluationStatus.RateUnavailable, evaluated.Evaluation.Status);
    }

    [Fact]
    public async Task Cancellation_propagates_instead_of_being_swallowed()
    {
        var store = new FakeAlertStore();
        store.Add(MakeAlert("GBP/CAD", 1.80m, AlertDirection.Above));

        var rateProvider = new RecordingRateProvider(new OperationCanceledException());
        var service = new AlertService(store, rateProvider, NullLogger<AlertService>.Instance);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.GetAllWithEvaluationsAsync(CancellationToken.None));
    }
}
