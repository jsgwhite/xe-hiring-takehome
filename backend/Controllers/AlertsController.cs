using Microsoft.AspNetCore.Mvc;
using RateAlerts.Api.Models;
using RateAlerts.Api.Services;

namespace RateAlerts.Api.Controllers;

[ApiController]
[Route("api/alerts")]
public class AlertsController : ControllerBase
{
    private readonly IAlertStore _store;
    private readonly AlertService _alertService;

    public AlertsController(IAlertStore store, AlertService alertService)
    {
        _store = store;
        _alertService = alertService;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var evaluations = await _alertService.GetAllWithEvaluationsAsync(cancellationToken);
        return Ok(evaluations.Select(ToDto));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAlertRequest request, CancellationToken cancellationToken)
    {
        if (!CurrencyPair.TryParse(request.Pair, out var pair))
        {
            return BadRequest(new { error = $"'{request.Pair}' is not a valid currency pair. Expected the form 'GBP/CAD'." });
        }

        // The stub this replaces accepted a negative threshold outright - verified against the
        // running app before this fix.
        if (request.Threshold <= 0)
        {
            return BadRequest(new { error = "Threshold must be greater than zero." });
        }

        if (!TryParseDirection(request.Direction, out var direction))
        {
            return BadRequest(new { error = "Direction must be 'above' or 'below'." });
        }

        var alert = _store.Add(new Alert(Guid.NewGuid(), pair, request.Threshold, direction, DateTimeOffset.UtcNow));
        var evaluation = await _alertService.EvaluateAsync(alert, cancellationToken);

        // The stub this replaces called CreatedAtAction(nameof(List), new { alert.Id, ... }) -
        // List takes no route parameters, so those values were silently dropped and every created
        // alert got a Location of /api/alerts, not a URI identifying the alert. Verified against the
        // running app before this fix. There is no single-alert GET in this API's contract (the
        // README documents only GET-list/POST/DELETE), so Location identifies the resource by id
        // without claiming a route that fetches it in isolation.
        return Created($"/api/alerts/{alert.Id}", ToDto(alert, evaluation));
    }

    [HttpDelete("{id:guid}")]
    public IActionResult Delete(Guid id)
        => _store.Remove(id) ? NoContent() : NotFound();

    private static bool TryParseDirection(string? value, out AlertDirection direction)
        => Enum.TryParse(value, ignoreCase: true, out direction) && Enum.IsDefined(direction);

    private static AlertDto ToDto(AlertWithEvaluation source) => ToDto(source.Alert, source.Evaluation);

    private static AlertDto ToDto(Alert alert, AlertEvaluation evaluation) => new(
        alert.Id,
        alert.Pair.ToString(),
        alert.Threshold,
        alert.Direction == AlertDirection.Above ? "above" : "below",
        evaluation.Triggered,
        evaluation.CurrentRate,
        evaluation.AsOf,
        evaluation.Status == EvaluationStatus.Ok ? "ok" : "rate_unavailable");
}
