using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace RateAlerts.Api.Controllers;

[ApiController]
[Route("api/rates")]
public class RatesController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public RatesController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpGet]
    public IActionResult GetRates()
    {
        var results = new List<object>();

        // USD/CAD
        var client = new HttpClient();
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(
            _configuration["Xecd:AccountId"] + ":" + _configuration["Xecd:ApiKey"]));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        var response = client.GetAsync("https://xecdapi.xe.com/v1/convert_from.json/?from=USD&to=CAD").Result;
        var body = response.Content.ReadAsStringAsync().Result;
        var doc = JsonDocument.Parse(body);
        var mid = doc.RootElement.GetProperty("to")[0].GetProperty("mid").GetDecimal();
        var timestamp = doc.RootElement.GetProperty("timestamp").GetString();
        results.Add(new { pair = "USD/CAD", rate = Math.Round(mid, 4), asOf = timestamp });

        // GBP/USD
        var client2 = new HttpClient();
        var credentials2 = Convert.ToBase64String(Encoding.ASCII.GetBytes(
            _configuration["Xecd:AccountId"] + ":" + _configuration["Xecd:ApiKey"]));
        client2.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials2);
        var response2 = client2.GetAsync("https://xecdapi.xe.com/v1/convert_from.json/?from=GBP&to=USD").Result;
        var body2 = response2.Content.ReadAsStringAsync().Result;
        var doc2 = JsonDocument.Parse(body2);
        var mid2 = doc2.RootElement.GetProperty("to")[0].GetProperty("mid").GetDecimal();
        var timestamp2 = doc2.RootElement.GetProperty("timestamp").GetString();
        results.Add(new { pair = "GBP/USD", rate = Math.Round(mid2, 4), asOf = timestamp2 });

        // EUR/USD
        var client3 = new HttpClient();
        var credentials3 = Convert.ToBase64String(Encoding.ASCII.GetBytes(
            _configuration["Xecd:AccountId"] + ":" + _configuration["Xecd:ApiKey"]));
        client3.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials3);
        var response3 = client3.GetAsync("https://xecdapi.xe.com/v1/convert_from.json/?from=EUR&to=USD").Result;
        var body3 = response3.Content.ReadAsStringAsync().Result;
        var doc3 = JsonDocument.Parse(body3);
        var mid3 = doc3.RootElement.GetProperty("to")[0].GetProperty("mid").GetDecimal();
        var timestamp3 = doc3.RootElement.GetProperty("timestamp").GetString();
        results.Add(new { pair = "EUR/USD", rate = Math.Round(mid3, 4), asOf = timestamp3 });

        return Ok(results);
    }
}
