namespace DeliveryQuote.Tests;

public class ReportTests
{
    private static PricedQuote[] SixQuotes()
    {
        var catalog = H.SampleCatalog();
        return H.SixValid.Select(r => H.Priced(catalog, Pricing.Standard, r)).ToArray();
    }

    [Fact]
    public void CategorySummaries_GroupsCountsAndSumsCorrectly()
    {
        var rows = Reports.CategorySummaries(SixQuotes());

        Assert.Equal(3, rows.Count);
        Assert.Equal(new CategorySummary("Electronics", 2, 112500), rows[0]);
        Assert.Equal(new CategorySummary("Furniture", 2, 93100), rows[1]);
        Assert.Equal(new CategorySummary("Accessories", 2, 27300), rows[2]);
    }

    [Fact]
    public void CategorySummaries_TiedTotals_AreOrderedByCategoryName()
    {
        var catalog = H.Value(Catalog.Create(new[]
        {
            H.Value(Product.Create("B1", "Beta item", "Beta", 1000, 10)),
            H.Value(Product.Create("A1", "Alpha item", "Alpha", 1000, 10)),
        }));
        var quotes = new[]
        {
            H.Priced(catalog, Pricing.Standard, H.Raw("R1", "B1", "1", "0")),
            H.Priced(catalog, Pricing.Standard, H.Raw("R2", "A1", "1", "0")),
        };

        var rows = Reports.CategorySummaries(quotes);

        Assert.Equal(rows[0].TotalCents, rows[1].TotalCents);
        Assert.Equal(new[] { "Alpha", "Beta" }, rows.Select(r => r.Category).ToArray());
    }

    [Fact]
    public void CategorySummaries_EmptyInput_GivesEmptyReport()
    {
        Assert.Empty(Reports.CategorySummaries(Array.Empty<PricedQuote>()));
    }

    [Fact]
    public void HighValueQuotes_FiltersOrdersAndProjects()
    {
        var rows = Reports.HighValueQuotes(SixQuotes(), 50_000);

        Assert.Equal(new[] { "R006", "R004" }, rows.Select(r => r.RequestId).ToArray());
        Assert.Equal("Standing Desk", rows[0].ProductName);
        Assert.Equal(75400, rows[0].TotalCents);
    }

    [Fact]
    public void HighValueQuotes_NoMatches_AndEmptyInput_GiveEmptyReport()
    {
        Assert.Empty(Reports.HighValueQuotes(SixQuotes(), 10_000_000));
        Assert.Empty(Reports.HighValueQuotes(Array.Empty<PricedQuote>(), 0));
    }

    [Fact]
    public void HighValueQuotes_TiedTotals_AreOrderedByRequestId()
    {
        var catalog = H.SampleCatalog();
        var quotes = new[]
        {
            H.Priced(catalog, Pricing.Standard, H.Raw("R2", "P001", "1", "0")),
            H.Priced(catalog, Pricing.Standard, H.Raw("R1", "P001", "1", "0")),
        };

        var rows = Reports.HighValueQuotes(quotes, 0);

        Assert.Equal(rows[0].TotalCents, rows[1].TotalCents);
        Assert.Equal(new[] { "R1", "R2" }, rows.Select(r => r.RequestId).ToArray());
    }

    [Fact]
    public void Reports_DoNotModifyTheirInput()
    {
        var quotes = SixQuotes();
        var before = quotes.Select(q => q.Request.RequestId).ToArray();

        _ = Reports.CategorySummaries(quotes);
        _ = Reports.HighValueQuotes(quotes, 0);

        Assert.Equal(before, quotes.Select(q => q.Request.RequestId).ToArray());
    }
}
