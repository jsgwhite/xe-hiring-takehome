using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using RateAlerts.Api.Controllers;
using RateAlerts.Api.Models;
using RateAlerts.Api.Services;

namespace RateAlerts.Api.Tests.Controllers;

public class AlertsControllerTests
{
    private static AlertsController CreateController(
        IAlertStore? store = null, IReadOnlyDictionary<CurrencyPair, decimal>? knownRates = null)
    {
        store ??= new InMemoryAlertStore();
        var rateProvider = new StubRateProvider(knownRates ?? new Dictionary<CurrencyPair, decimal>());
        var service = new AlertService(store, rateProvider, NullLogger<AlertService>.Instance);
        return new AlertsController(store, service);
    }

    private sealed class StubRateProvider : IRateProvider
    {
        private readonly IReadOnlyDictionary<CurrencyPair, decimal> _knownRates;

        public StubRateProvider(IReadOnlyDictionary<CurrencyPair, decimal> knownRates) => _knownRates = knownRates;

        public Task<IReadOnlyDictionary<CurrencyPair, Rate>> GetRatesAsync(
            IReadOnlyCollection<CurrencyPair> pairs, CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            IReadOnlyDictionary<CurrencyPair, Rate> result = pairs
                .Where(_knownRates.ContainsKey)
                .ToDictionary(pair => pair, pair => new Rate(pair, _knownRates[pair], now));
            return Task.FromResult(result);
        }
    }

    [Fact]
    public async Task Create_with_a_valid_request_returns_201_with_a_Location_identifying_the_alert()
    {
        var controller = CreateController(knownRates: new Dictionary<CurrencyPair, decimal>
        {
            [CurrencyPair.Parse("GBP/CAD")] = 1.85m,
        });

        var result = await controller.Create(
            new CreateAlertRequest("GBP/CAD", 1.84m, "above"), CancellationToken.None);

        var created = Assert.IsType<CreatedResult>(result);
        var dto = Assert.IsType<AlertDto>(created.Value);

        // The stub this replaces passed the alert as route values to a parameterless action,
        // so Location came back as /api/alerts - missing the id entirely. Verified against the
        // running app before this fix.
        Assert.Equal($"/api/alerts/{dto.Id}", created.Location);
        Assert.Equal("GBP/CAD", dto.Pair);
        Assert.Equal("above", dto.Direction);
        Assert.True(dto.Triggered);
    }

    [Fact]
    public async Task Create_persists_the_alert_so_a_later_List_sees_it()
    {
        var store = new InMemoryAlertStore();
        var controller = CreateController(store);

        controller = CreateController(store, new Dictionary<CurrencyPair, decimal>
        {
            [CurrencyPair.Parse("USD/CAD")] = 1.35m,
        });

        await controller.Create(new CreateAlertRequest("USD/CAD", 1.30m, "above"), CancellationToken.None);
        var listResult = await controller.List(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(listResult);
        var dtos = Assert.IsAssignableFrom<IEnumerable<AlertDto>>(ok.Value);
        Assert.Single(dtos);
    }

    [Theory]
    [InlineData("GBPCAD")]   // missing separator
    [InlineData("GBP/GBP")]  // currency paired with itself
    [InlineData("")]
    public async Task Create_rejects_an_invalid_pair_with_400(string pair)
    {
        var controller = CreateController();

        var result = await controller.Create(new CreateAlertRequest(pair, 1.5m, "above"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Create_rejects_a_well_formed_but_unknown_currency_pair_without_persisting_it()
    {
        var store = new InMemoryAlertStore();
        var controller = CreateController(store);

        var result = await controller.Create(
            new CreateAlertRequest("EUR/USX", 1.08m, "above"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(store.GetAll());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Create_rejects_a_non_positive_threshold_with_400(decimal threshold)
    {
        // The stub this replaces accepted a negative threshold outright. Verified against the
        // running app before this fix.
        var controller = CreateController();

        var result = await controller.Create(
            new CreateAlertRequest("GBP/CAD", threshold, "above"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Theory]
    [InlineData("sideways")]
    [InlineData("")]
    public async Task Create_rejects_an_invalid_direction_with_400(string direction)
    {
        var controller = CreateController();

        var result = await controller.Create(
            new CreateAlertRequest("GBP/CAD", 1.5m, direction), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Theory]
    [InlineData("above")]
    [InlineData("Above")]
    [InlineData("ABOVE")]
    [InlineData("below")]
    public async Task Create_accepts_direction_case_insensitively(string direction)
    {
        var controller = CreateController(knownRates: new Dictionary<CurrencyPair, decimal>
        {
            [CurrencyPair.Parse("GBP/CAD")] = 1.85m,
        });

        var result = await controller.Create(new CreateAlertRequest("GBP/CAD", 1.5m, direction), CancellationToken.None);

        Assert.IsType<CreatedResult>(result);
    }

    [Fact]
    public async Task List_returns_an_empty_array_when_no_alerts_exist()
    {
        var controller = CreateController();

        var result = await controller.List(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dtos = Assert.IsAssignableFrom<IEnumerable<AlertDto>>(ok.Value);
        Assert.Empty(dtos);
    }

    [Fact]
    public async Task List_reports_rate_unavailable_status_for_a_pair_the_provider_has_no_rate_for()
    {
        var store = new InMemoryAlertStore();
        store.Add(new Alert(Guid.NewGuid(), CurrencyPair.Parse("GBP/CAD"), 1.84m, AlertDirection.Above, DateTimeOffset.UtcNow));
        var controller = CreateController(store); // no known rates

        var result = await controller.List(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.Single(Assert.IsAssignableFrom<IEnumerable<AlertDto>>(ok.Value));
        Assert.Equal("rate_unavailable", dto.Status);
        Assert.False(dto.Triggered);
    }

    [Fact]
    public void Delete_of_an_existing_alert_returns_204()
    {
        var store = new InMemoryAlertStore();
        var alert = store.Add(new Alert(Guid.NewGuid(), CurrencyPair.Parse("GBP/CAD"), 1.84m, AlertDirection.Above, DateTimeOffset.UtcNow));
        var controller = CreateController(store);

        var result = controller.Delete(alert.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.Empty(store.GetAll());
    }

    [Fact]
    public void Delete_of_an_unknown_id_returns_404()
    {
        var controller = CreateController();

        var result = controller.Delete(Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result);
    }
}
