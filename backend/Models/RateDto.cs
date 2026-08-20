namespace RateAlerts.Api.Models;

/// <summary>Wire contract for GET /api/rates. Replaces the anonymous objects the endpoint used to return.</summary>
public sealed record RateDto(string Pair, decimal Rate, DateTimeOffset AsOf)
{
    public static RateDto FromRate(Rate rate) => new(rate.Pair.ToString(), rate.Mid, rate.AsOf);
}
