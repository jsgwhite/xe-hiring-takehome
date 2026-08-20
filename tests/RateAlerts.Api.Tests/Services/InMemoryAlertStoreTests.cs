using RateAlerts.Api.Models;
using RateAlerts.Api.Services;

namespace RateAlerts.Api.Tests.Services;

public class InMemoryAlertStoreTests
{
    [Fact]
    public void GetAll_on_empty_store_returns_empty()
    {
        var store = new InMemoryAlertStore();

        var result = store.GetAll();

        Assert.Empty(result);
    }

    [Fact]
    public void Add_then_GetAll_returns_the_added_alert()
    {
        var store = new InMemoryAlertStore();
        var alert = new Alert(Guid.NewGuid(), CurrencyPair.Parse("GBP/CAD"), 1.84m, AlertDirection.Above, DateTimeOffset.UtcNow);

        store.Add(alert);
        var result = store.GetAll();

        Assert.Single(result);
        Assert.Contains(alert, result);
    }

    [Fact]
    public void Add_returns_the_same_alert_that_was_passed_in()
    {
        var store = new InMemoryAlertStore();
        var alert = new Alert(Guid.NewGuid(), CurrencyPair.Parse("GBP/CAD"), 1.84m, AlertDirection.Above, DateTimeOffset.UtcNow);

        var returned = store.Add(alert);

        Assert.Equal(alert, returned);
    }

    [Fact]
    public void GetAll_returns_alerts_ordered_by_CreatedAt_ascending()
    {
        var store = new InMemoryAlertStore();
        var baseTime = DateTimeOffset.UtcNow;
        var alert3 = new Alert(Guid.NewGuid(), CurrencyPair.Parse("GBP/CAD"), 1.84m, AlertDirection.Above, baseTime.AddSeconds(30));
        var alert1 = new Alert(Guid.NewGuid(), CurrencyPair.Parse("GBP/CAD"), 1.84m, AlertDirection.Above, baseTime);
        var alert2 = new Alert(Guid.NewGuid(), CurrencyPair.Parse("GBP/CAD"), 1.84m, AlertDirection.Above, baseTime.AddSeconds(15));

        store.Add(alert3);
        store.Add(alert1);
        store.Add(alert2);

        var result = store.GetAll();

        Assert.Equal([alert1, alert2, alert3], result);
    }

    [Fact]
    public void Remove_existing_id_returns_true_and_it_is_gone_from_GetAll()
    {
        var store = new InMemoryAlertStore();
        var alert = new Alert(Guid.NewGuid(), CurrencyPair.Parse("GBP/CAD"), 1.84m, AlertDirection.Above, DateTimeOffset.UtcNow);
        store.Add(alert);

        var removed = store.Remove(alert.Id);

        Assert.True(removed);
        Assert.Empty(store.GetAll());
    }

    [Fact]
    public void Remove_non_existent_id_returns_false_and_does_not_affect_existing_alerts()
    {
        var store = new InMemoryAlertStore();
        var alert = new Alert(Guid.NewGuid(), CurrencyPair.Parse("GBP/CAD"), 1.84m, AlertDirection.Above, DateTimeOffset.UtcNow);
        store.Add(alert);

        var removed = store.Remove(Guid.NewGuid());

        Assert.False(removed);
        Assert.Single(store.GetAll());
        Assert.Contains(alert, store.GetAll());
    }

    [Fact]
    public void Adding_two_alerts_with_different_ids_both_appear_in_GetAll()
    {
        var store = new InMemoryAlertStore();
        var alert1 = new Alert(Guid.NewGuid(), CurrencyPair.Parse("GBP/CAD"), 1.84m, AlertDirection.Above, DateTimeOffset.UtcNow);
        var alert2 = new Alert(Guid.NewGuid(), CurrencyPair.Parse("USD/CAD"), 1.26m, AlertDirection.Below, DateTimeOffset.UtcNow);

        store.Add(alert1);
        store.Add(alert2);

        var result = store.GetAll();

        Assert.Equal(2, result.Count);
        Assert.Contains(alert1, result);
        Assert.Contains(alert2, result);
    }
}
