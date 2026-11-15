namespace DeliveryQuote;

public sealed record CategorySummary(string Category, int QuoteCount, long TotalCents);
public sealed record HighValueRow(string RequestId, string ProductName, long TotalCents);

public static class Reports
{
    public static IReadOnlyList<CategorySummary> CategorySummaries(IEnumerable<PricedQuote> quotes) =>
        quotes
            .GroupBy(q => q.Request.Product.Category)
            .Select(g => new CategorySummary(g.Key, g.Count(), g.Sum(q => q.TotalCents)))
            .OrderByDescending(s => s.TotalCents)
            .ThenBy(s => s.Category, StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<HighValueRow> HighValueQuotes(IEnumerable<PricedQuote> quotes, long minTotalCents) =>
        quotes
            .HighValue(minTotalCents)
            .OrderByDescending(q => q.TotalCents)
            .ThenBy(q => q.Request.RequestId, StringComparer.Ordinal)
            .Select(q => new HighValueRow(q.Request.RequestId, q.Request.Product.Name, q.TotalCents))
            .ToArray();
}
