using RateAlerts.Api.Models;

namespace RateAlerts.Api.Tests.Models;

public class CurrencyPairTests
{
    [Theory]
    [InlineData("GBP/CAD", "GBP", "CAD")]
    [InlineData("gbp/cad", "GBP", "CAD")]
    [InlineData("Gbp/CaD", "GBP", "CAD")]
    [InlineData(" GBP / CAD ", "GBP", "CAD")]
    [InlineData("AUD/JPY", "AUD", "JPY")]
    public void TryParse_accepts_valid_pairs_and_normalises_to_upper_case(
        string input, string expectedBase, string expectedQuote)
    {
        Assert.True(CurrencyPair.TryParse(input, out var pair));
        Assert.Equal(expectedBase, pair.Base);
        Assert.Equal(expectedQuote, pair.Quote);
    }

    [Theory]
    [InlineData(null)]              // no input at all
    [InlineData("")]                // empty
    [InlineData("   ")]             // whitespace only
    [InlineData("GBPCAD")]          // missing separator
    [InlineData("GB/CAD")]          // base too short
    [InlineData("GBPP/CAD")]        // base too long
    [InlineData("GBP/CA")]          // quote too short
    [InlineData("GBP/")]            // quote missing
    [InlineData("/CAD")]            // base missing
    [InlineData("/")]               // both missing
    [InlineData("GBP/CAD/USD")]     // too many parts: must not silently truncate
    [InlineData("GB1/CAD")]         // digit in code
    [InlineData("GB-/CAD")]         // punctuation in code
    [InlineData("ГБП/CAD")]         // non-ASCII letters the upstream API would reject
    public void TryParse_rejects_malformed_input(string? input)
    {
        Assert.False(CurrencyPair.TryParse(input, out var pair));
        Assert.Null(pair);
    }

    [Theory]
    [InlineData("GBP/GBP")]
    [InlineData("gbp/GBP")]
    public void TryParse_rejects_a_currency_paired_with_itself(string input)
    {
        // The rate is always 1, so an alert on it could never say anything useful.
        Assert.False(CurrencyPair.TryParse(input, out _));
    }

    [Fact]
    public void ToString_round_trips_through_TryParse()
    {
        Assert.True(CurrencyPair.TryParse("gbp/cad", out var pair));
        Assert.Equal("GBP/CAD", pair.ToString());

        Assert.True(CurrencyPair.TryParse(pair.ToString(), out var reparsed));
        Assert.Equal(pair, reparsed);
    }

    [Fact]
    public void Pairs_differing_only_by_case_are_equal()
    {
        // Matters because CurrencyPair is used as a dictionary key when batching rate lookups.
        var lower = CurrencyPair.Parse("gbp/cad");
        var upper = CurrencyPair.Parse("GBP/CAD");

        Assert.Equal(lower, upper);
        Assert.Equal(lower.GetHashCode(), upper.GetHashCode());

        var byPair = new Dictionary<CurrencyPair, decimal> { [lower] = 1.71m };
        Assert.True(byPair.ContainsKey(upper));
    }

    [Fact]
    public void Direction_is_significant()
    {
        Assert.NotEqual(CurrencyPair.Parse("GBP/CAD"), CurrencyPair.Parse("CAD/GBP"));
    }

    [Fact]
    public void Parse_throws_on_invalid_input()
    {
        var exception = Assert.Throws<ArgumentException>(() => CurrencyPair.Parse("GBPCAD"));
        Assert.Contains("GBPCAD", exception.Message);
    }

    [Fact]
    public void TryCreate_accepts_separate_codes()
    {
        Assert.True(CurrencyPair.TryCreate("usd", "jpy", out var pair));
        Assert.Equal("USD/JPY", pair.ToString());
    }

    [Fact]
    public void Create_throws_when_both_codes_are_the_same_currency()
    {
        Assert.Throws<ArgumentException>(() => CurrencyPair.Create("USD", "usd"));
    }

    [Fact]
    public void Create_returns_pair_with_valid_separate_codes()
    {
        var pair = CurrencyPair.Create("USD", "EUR");
        Assert.NotNull(pair);
        Assert.Equal("USD", pair.Base);
        Assert.Equal("EUR", pair.Quote);
    }

    [Fact]
    public void TryCreate_rejects_null_base()
    {
        Assert.False(CurrencyPair.TryCreate(null, "USD", out var pair));
        Assert.Null(pair);
    }

    [Fact]
    public void TryCreate_rejects_null_quote()
    {
        Assert.False(CurrencyPair.TryCreate("USD", null, out var pair));
        Assert.Null(pair);
    }
}
