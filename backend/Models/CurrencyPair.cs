using System.Diagnostics.CodeAnalysis;

namespace RateAlerts.Api.Models;

/// <summary>
/// A base/quote currency pair, e.g. GBP/CAD.
/// </summary>
/// <remarks>
/// Base and quote are held separately rather than as a single "GBP/CAD" string for three reasons:
/// they map one-to-one onto the Xe API's <c>from</c>/<c>to</c> parameters; holding them apart is what
/// lets us group several pairs sharing a base currency into a single upstream request; and it turns
/// "support any pair" into a validation concern rather than a schema change.
///
/// Codes are normalised to upper case on construction, so GBP/CAD and gbp/cad are the same instance
/// as far as equality and dictionary lookup are concerned.
/// </remarks>
public sealed record CurrencyPair
{
    private const int CurrencyCodeLength = 3;

    private CurrencyPair(string @base, string quote)
    {
        Base = @base;
        Quote = quote;
    }

    /// <summary>The currency being converted from, e.g. GBP. Always upper case.</summary>
    public string Base { get; }

    /// <summary>The currency being converted to, e.g. CAD. Always upper case.</summary>
    public string Quote { get; }

    /// <summary>
    /// Creates a pair from two currency codes, throwing if either is not a valid ISO-style
    /// three-letter code or if the two are the same currency.
    /// </summary>
    public static CurrencyPair Create(string? @base, string? quote)
        => TryCreate(@base, quote, out var pair)
            ? pair
            : throw new ArgumentException($"Invalid currency pair '{@base}/{quote}'.");

    /// <summary>
    /// Parses the canonical "BASE/QUOTE" form, e.g. "GBP/CAD", throwing if it is not valid.
    /// </summary>
    public static CurrencyPair Parse(string? value)
        => TryParse(value, out var pair)
            ? pair
            : throw new ArgumentException($"Invalid currency pair '{value}'. Expected the form 'GBP/CAD'.");

    /// <summary>
    /// Parses the canonical "BASE/QUOTE" form. Returns false rather than throwing on bad input, so
    /// request validation can turn a failure into a 400 without exception handling.
    /// </summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out CurrencyPair? pair)
    {
        pair = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        // A single separator, so "GBP/CAD/USD" is rejected rather than silently truncated.
        var parts = value.Split('/');
        return parts.Length == 2 && TryCreate(parts[0], parts[1], out pair);
    }

    /// <summary>
    /// Creates a pair from two currency codes. Returns false rather than throwing on bad input.
    /// </summary>
    public static bool TryCreate(string? @base, string? quote, [NotNullWhen(true)] out CurrencyPair? pair)
    {
        pair = null;

        if (!TryNormaliseCode(@base, out var normalisedBase) ||
            !TryNormaliseCode(quote, out var normalisedQuote))
        {
            return false;
        }

        // A pair of a currency with itself always has a rate of 1, so an alert on it is meaningless.
        if (normalisedBase == normalisedQuote)
        {
            return false;
        }

        pair = new CurrencyPair(normalisedBase, normalisedQuote);
        return true;
    }

    private static bool TryNormaliseCode(string? code, [NotNullWhen(true)] out string? normalised)
    {
        normalised = null;

        if (code is null)
        {
            return false;
        }

        var trimmed = code.Trim();
        if (trimmed.Length != CurrencyCodeLength)
        {
            return false;
        }

        // Deliberately ASCII-only: ISO 4217 codes are A-Z, and char.IsLetter would accept
        // non-Latin letters that the upstream API would then reject.
        foreach (var character in trimmed)
        {
            if (!char.IsAsciiLetter(character))
            {
                return false;
            }
        }

        normalised = trimmed.ToUpperInvariant();
        return true;
    }

    /// <summary>The canonical "BASE/QUOTE" form, e.g. "GBP/CAD". This is what we put on the wire.</summary>
    public override string ToString() => $"{Base}/{Quote}";
}
