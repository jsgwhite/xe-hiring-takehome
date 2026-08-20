using Microsoft.AspNetCore.Mvc;
using RateAlerts.Api.Models;
using RateAlerts.Api.Services;

namespace RateAlerts.Api.Controllers;

[ApiController]
[Route("api/rates")]
public class RatesController : ControllerBase
{
    private readonly IRateProvider _rateProvider;

    public RatesController(IRateProvider rateProvider)
    {
        _rateProvider = rateProvider;
    }

    [HttpGet]
    public async Task<IActionResult> GetRates(CancellationToken cancellationToken)
    {
        var rates = await _rateProvider.GetRatesAsync(DisplayedPairs.All, cancellationToken);

        // A pair missing from the result (upstream failure) is left out rather than crashing the
        // whole board - the frontend already shows "..." for a pair it has no data for.
        var response = DisplayedPairs.All
            .Where(rates.ContainsKey)
            .Select(pair => RateDto.FromRate(rates[pair]))
            .ToList();

        return Ok(response);
    }
}
