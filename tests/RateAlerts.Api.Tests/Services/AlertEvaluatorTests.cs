using RateAlerts.Api.Models;
using RateAlerts.Api.Services;

namespace RateAlerts.Api.Tests.Services;

public class AlertEvaluatorTests
{
    private static Alert AboveAlert(decimal threshold) => new(
        Guid.NewGuid(), CurrencyPair.Parse("GBP/CAD"), threshold, AlertDirection.Above, DateTimeOffset.UtcNow);

    private static Alert BelowAlert(decimal threshold) => new(
        Guid.NewGuid(), CurrencyPair.Parse("GBP/CAD"), threshold, AlertDirection.Below, DateTimeOffset.UtcNow);

    private static Rate RateOf(decimal mid) =>
        new(CurrencyPair.Parse("GBP/CAD"), mid, DateTimeOffset.UtcNow);

    [Fact]
    public void Above_triggers_when_rate_exceeds_threshold()
    {
        var result = AlertEvaluator.Evaluate(AboveAlert(1.84m), RateOf(1.85m));

        Assert.True(result.Triggered);
        Assert.Equal(EvaluationStatus.Ok, result.Status);
        Assert.Equal(1.85m, result.CurrentRate);
    }

    [Fact]
    public void Above_does_not_trigger_when_rate_is_below_threshold()
    {
        var result = AlertEvaluator.Evaluate(AboveAlert(1.84m), RateOf(1.83m));

        Assert.False(result.Triggered);
    }

    [Fact]
    public void Above_does_not_trigger_on_exact_equality()
    {
        // Deliberate: a rate sitting exactly on the threshold has not gone "above" it. Equality is
        // the boundary, and it belongs to neither direction.
        var result = AlertEvaluator.Evaluate(AboveAlert(1.84m), RateOf(1.84m));

        Assert.False(result.Triggered);
    }

    [Fact]
    public void Below_triggers_when_rate_is_under_threshold()
    {
        var result = AlertEvaluator.Evaluate(BelowAlert(1.84m), RateOf(1.83m));

        Assert.True(result.Triggered);
    }

    [Fact]
    public void Below_does_not_trigger_when_rate_is_above_threshold()
    {
        var result = AlertEvaluator.Evaluate(BelowAlert(1.84m), RateOf(1.85m));

        Assert.False(result.Triggered);
    }

    [Fact]
    public void Below_does_not_trigger_on_exact_equality()
    {
        // Same boundary rule, other direction - see Above_does_not_trigger_on_exact_equality.
        var result = AlertEvaluator.Evaluate(BelowAlert(1.84m), RateOf(1.84m));

        Assert.False(result.Triggered);
    }

    [Fact]
    public void Missing_rate_is_reported_as_unavailable_not_as_not_triggered()
    {
        // A missing rate means "unknown", not "false". Collapsing it into Triggered=false would let
        // an upstream outage silently read as "nothing to worry about".
        var result = AlertEvaluator.Evaluate(AboveAlert(1.84m), rate: null);

        Assert.False(result.Triggered);
        Assert.Equal(EvaluationStatus.RateUnavailable, result.Status);
        Assert.Null(result.CurrentRate);
        Assert.Null(result.AsOf);
    }

    [Fact]
    public void Result_carries_the_rate_timestamp_when_a_rate_is_present()
    {
        var asOf = DateTimeOffset.Parse("2026-08-19T23:53:00Z");
        var result = AlertEvaluator.Evaluate(AboveAlert(1.84m), new Rate(CurrencyPair.Parse("GBP/CAD"), 1.85m, asOf));

        Assert.Equal(asOf, result.AsOf);
    }

    [Fact]
    public void Throws_when_the_rate_is_for_a_different_pair_than_the_alert()
    {
        // Otherwise a EUR/USD rate handed to a GBP/CAD alert would be accepted silently and produce
        // a confident, wrong answer - Mid is just a decimal, nothing else would catch the mismatch.
        var alert = AboveAlert(1.84m);
        var wrongPairRate = new Rate(CurrencyPair.Parse("EUR/USD"), 1.08m, DateTimeOffset.UtcNow);

        var exception = Assert.Throws<ArgumentException>(() => AlertEvaluator.Evaluate(alert, wrongPairRate));
        Assert.Contains("EUR/USD", exception.Message);
        Assert.Contains("GBP/CAD", exception.Message);
    }

    [Theory]
    [InlineData(1.8399, false)]
    [InlineData(1.8400, false)]
    [InlineData(1.8401, true)]
    public void Above_is_precise_at_four_decimal_places(decimal rate, bool expectedTriggered)
    {
        var result = AlertEvaluator.Evaluate(AboveAlert(1.84m), RateOf(rate));

        Assert.Equal(expectedTriggered, result.Triggered);
    }
}
