using RateAlerts.Api.Models;

namespace RateAlerts.Api.Services;

public sealed record AlertWithEvaluation(Alert Alert, AlertEvaluation Evaluation);

/// <summary>
/// Orchestrates listing alerts alongside their current triggered state: collects the distinct pairs
/// referenced by stored alerts and fetches them in a single batched call, rather than one rate
/// lookup per alert. If the rate provider fails outright, alerts still list - each with
/// EvaluationStatus.RateUnavailable - rather than the whole request failing.
/// </summary>
public sealed class AlertService
{
    private readonly IAlertStore _store;
    private readonly IRateProvider _rateProvider;
    private readonly ILogger<AlertService> _logger;

    public AlertService(IAlertStore store, IRateProvider rateProvider, ILogger<AlertService> logger)
    {
        _store = store;
        _rateProvider = rateProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AlertWithEvaluation>> GetAllWithEvaluationsAsync(CancellationToken cancellationToken)
    {
        var alerts = _store.GetAll();
        if (alerts.Count == 0)
        {
            return [];
        }

        var pairs = alerts.Select(alert => alert.Pair).Distinct().ToList();
        var rates = await FetchRatesAsync(pairs, cancellationToken);

        return alerts
            .Select(alert => new AlertWithEvaluation(alert, AlertEvaluator.Evaluate(alert, rates.GetValueOrDefault(alert.Pair))))
            .ToList();
    }

    /// <summary>Evaluates a single alert - used right after creating one, where re-running the full
    /// batched list evaluation just to return one row would be wasteful.</summary>
    public async Task<AlertEvaluation> EvaluateAsync(Alert alert, CancellationToken cancellationToken)
    {
        var rates = await FetchRatesAsync([alert.Pair], cancellationToken);
        return AlertEvaluator.Evaluate(alert, rates.GetValueOrDefault(alert.Pair));
    }

    private async Task<IReadOnlyDictionary<CurrencyPair, Rate>> FetchRatesAsync(
        IReadOnlyList<CurrencyPair> pairs, CancellationToken cancellationToken)
    {
        try
        {
            return await _rateProvider.GetRatesAsync(pairs, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The registered IRateProvider implementations already swallow their own upstream
            // failures per pair, but this guards the case regardless: a rate-provider exception
            // must not turn "list my alerts" into a 500. Every alert falls back to
            // EvaluationStatus.RateUnavailable via the empty dictionary below.
            _logger.LogWarning(ex, "Rate provider failed while evaluating alerts");
            return new Dictionary<CurrencyPair, Rate>();
        }
    }
}
