using Microsoft.AspNetCore.Mvc;

namespace RateAlerts.Api.Controllers;

// =====================================================================================
// STUB CONTROLLER - provided for the FRONTEND track.
//
// This gives frontend candidates a working /api/alerts API so they can build the alert
// management UI without writing backend code. It keeps alerts in a static in-memory
// list and evaluates the "triggered" flag against the canned rates below, so creating
// an alert with a threshold on the wrong side of the canned rate will show as triggered.
//
// BACKEND-TRACK CANDIDATES: this is not a partial solution and you are not expected to
// keep it. Replace it or delete it; the alert feature is yours to design.
// =====================================================================================

[ApiController]
[Route("api/alerts")]
public class AlertsStubController : ControllerBase
{
    private static readonly Dictionary<string, decimal> CannedRates = new()
    {
        ["USD/CAD"] = 1.3650m,
        ["GBP/USD"] = 1.2710m,
        ["EUR/USD"] = 1.0830m,
    };

    private static readonly List<Alert> Alerts = new();
    private static readonly Lock Sync = new();

    [HttpGet]
    public IActionResult List()
    {
        lock (Sync)
        {
            var view = Alerts
                .Select(a => new
                {
                    a.Id,
                    a.Pair,
                    a.Threshold,
                    a.Direction,
                    Triggered = IsTriggered(a),
                })
                .ToList();
            return Ok(view);
        }
    }

    [HttpPost]
    public IActionResult Create([FromBody] CreateAlertRequest request)
    {
        if (!CannedRates.ContainsKey(request.Pair))
        {
            return BadRequest(new { error = $"Unknown pair '{request.Pair}'. The stub supports: {string.Join(", ", CannedRates.Keys)}." });
        }

        if (request.Direction is not ("above" or "below"))
        {
            return BadRequest(new { error = "Direction must be 'above' or 'below'." });
        }

        var alert = new Alert(Guid.NewGuid(), request.Pair, request.Threshold, request.Direction);
        lock (Sync)
        {
            Alerts.Add(alert);
        }

        return CreatedAtAction(nameof(List), new { alert.Id, alert.Pair, alert.Threshold, alert.Direction, Triggered = IsTriggered(alert) });
    }

    [HttpDelete("{id:guid}")]
    public IActionResult Delete(Guid id)
    {
        lock (Sync)
        {
            var removed = Alerts.RemoveAll(a => a.Id == id);
            return removed > 0 ? NoContent() : NotFound();
        }
    }

    private static bool IsTriggered(Alert alert)
    {
        var rate = CannedRates[alert.Pair];
        return alert.Direction == "above" ? rate > alert.Threshold : rate < alert.Threshold;
    }

    public record Alert(Guid Id, string Pair, decimal Threshold, string Direction);

    public record CreateAlertRequest(string Pair, decimal Threshold, string Direction);
}
