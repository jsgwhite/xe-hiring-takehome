using RateAlerts.Api.Models;

namespace RateAlerts.Api.Services;

public enum EvaluationStatus
{
    Ok,
    RateUnavailable,
}

/// <summary>Result of evaluating one alert against a rate. Never constructed with Triggered=true and
/// Status=RateUnavailable at once - see AlertEvaluator.Evaluate.</summary>
public sealed record AlertEvaluation(bool Triggered, decimal? CurrentRate, DateTimeOffset? AsOf, EvaluationStatus Status);

/// <summary>
/// Pure evaluation of a single alert against a single rate. No I/O, no DI - deliberately, so the
/// core "did this fire" logic can be tested in isolation from where the rate came from.
/// </summary>
public static class AlertEvaluator
{
    public static AlertEvaluation Evaluate(Alert alert, Rate? rate)
    {
        if (rate is null)
        {
            // Unknown, not "not triggered" - collapsing the two would make an upstream outage read
            // as "everything's fine".
            return new AlertEvaluation(Triggered: false, CurrentRate: null, AsOf: null, EvaluationStatus.RateUnavailable);
        }

        // Exact equality belongs to neither direction: a rate sitting exactly on the threshold has
        // not gone "above" it, and has not dropped "below" it either.
        var triggered = alert.Direction == AlertDirection.Above
            ? rate.Mid > alert.Threshold
            : rate.Mid < alert.Threshold;

        return new AlertEvaluation(triggered, rate.Mid, rate.AsOf, EvaluationStatus.Ok);
    }
}
