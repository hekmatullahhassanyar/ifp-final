namespace DeliveryQuote.Tests;

public class WorkflowTests
{
    [Fact]
    public void Pipeline_ValidRequest_ProducesPendingQuoteWithDocumentedTotal()
    {
        var state = H.Value(QuoteWorkflow.Quote(H.Raw("R002", "P003", "5", "30"), H.SampleCatalog(), Pricing.Standard, H.Stub));
        var quote = Assert.IsType<DeliveryState.Pending>(state).Quote;

        Assert.Equal(16000, quote.ItemsCents);
        Assert.Equal(15200, quote.DiscountedItemsCents);
        Assert.Equal(3500, quote.ShippingCents);
        Assert.Equal(18700, quote.TotalCents);
    }

    [Fact]
    public void Pipeline_InvalidRequest_SkipsPricingStage()
    {
        var discountCalls = 0;
        var result = QuoteWorkflow.Quote(H.Raw("R1", "P001", "0", "1"), H.SampleCatalog(), Pricing.Standard,
            (a, p) => { discountCalls++; return H.StubDiscount(a, p); });

        Assert.Equal("INVALID_QUANTITY", H.Error(result).Code);
        Assert.Equal(0, discountCalls);
    }

    [Fact]
    public void Pipeline_SamePolicyAndInput_IsDeterministicAndPoliciesDiffer()
    {
        var catalog = H.SampleCatalog();
        var raw = H.Raw("R001", "P001", "2", "2");

        var standard1 = H.Value(QuoteWorkflow.Quote(raw, catalog, Pricing.Standard, H.Stub));
        var standard2 = H.Value(QuoteWorkflow.Quote(raw, catalog, Pricing.Standard, H.Stub));
        var express = H.Value(QuoteWorkflow.Quote(raw, catalog, Pricing.Express, H.Stub));

        Assert.Equal(standard1, standard2);
        Assert.Equal(17700, Assert.IsType<DeliveryState.Pending>(standard1).Quote.TotalCents);
        Assert.Equal(18100, Assert.IsType<DeliveryState.Pending>(express).Quote.TotalCents);
    }

    [Fact]
    public void Pipeline_EmptyRequestList_ProducesEmptyResult()
    {
        var results = QuoteWorkflow.QuoteAll(Array.Empty<RawRequest>(), H.SampleCatalog(), Pricing.Standard, H.Stub).ToArray();
        Assert.Empty(results);
    }

    [Fact]
    public void Confirm_Pending_ReservesStockInNewCatalogOnly()
    {
        var catalog = H.SampleCatalog();
        var pending = H.Value(QuoteWorkflow.Quote(H.Raw("R004", "P005", "3", "120"), catalog, Pricing.Standard, H.Stub));

        var outcome = H.Value(Fulfilment.Confirm(pending, catalog));

        Assert.Equal("CONF-R004", outcome.Order.ConfirmationId);
        Assert.Equal(1, Assert.IsType<Maybe<Product>.Some>(outcome.Catalog.Find("P005")).Value.Stock);
        Assert.Equal(4, Assert.IsType<Maybe<Product>.Some>(catalog.Find("P005")).Value.Stock);
    }

    [Fact]
    public void Confirm_AlreadyConfirmed_IsRejected()
    {
        var catalog = H.SampleCatalog();
        var pending = H.Value(QuoteWorkflow.Quote(H.Raw("R004", "P005", "1", "10"), catalog, Pricing.Standard, H.Stub));
        var first = H.Value(Fulfilment.Confirm(pending, catalog));

        var second = Fulfilment.Confirm(first.Order, first.Catalog);

        Assert.Equal("ALREADY_CONFIRMED", H.Error(second).Code);
        Assert.Equal(3, Assert.IsType<Maybe<Product>.Some>(first.Catalog.Find("P005")).Value.Stock);
    }

    [Fact]
    public void ConfirmAll_StockExhausted_LaterConfirmationFailsAndCatalogStaysConsistent()
    {
        var catalog = H.SampleCatalog();
        var states = new[] { H.Raw("R004", "P005", "3", "120"), H.Raw("R005", "P005", "2", "50") }
            .Select(r => H.Value(QuoteWorkflow.Quote(r, catalog, Pricing.Standard, H.Stub)))
            .ToArray();

        var (updated, orders) = Fulfilment.ConfirmAll(catalog, states);

        Assert.Equal("CONF-R004", H.Value(orders[0]).ConfirmationId);
        Assert.Equal("INSUFFICIENT_STOCK", H.Error(orders[1]).Code);
        Assert.Equal(1, Assert.IsType<Maybe<Product>.Some>(updated.Find("P005")).Value.Stock);
        Assert.Equal(4, Assert.IsType<Maybe<Product>.Some>(catalog.Find("P005")).Value.Stock);
    }

    [Fact]
    public void CompleteWorkflow_RealNativeCall_QuotesConfirmsAndReports()
    {
        var adapter = new NativeDiscountAdapter();
        Func<long, int, Result<long>> discount = adapter.Apply;
        var catalog = H.SampleCatalog();

        var results = QuoteWorkflow.QuoteAll(H.SixValid, catalog, Pricing.Standard, discount).ToArray();
        var states = results.Successes().ToArray();
        var quotes = states.OfType<DeliveryState.Pending>().Select(p => p.Quote).ToArray();

        Assert.Equal(6, states.Length);
        Assert.Equal(8600, quotes.Single(q => q.Request.RequestId == "R003").TotalCents);
        Assert.Equal(18700, quotes.Single(q => q.Request.RequestId == "R002").TotalCents);

        var summary = Reports.CategorySummaries(quotes);
        Assert.Equal(new[] { "Electronics", "Furniture", "Accessories" }, summary.Select(s => s.Category).ToArray());
        Assert.Equal(new long[] { 112500, 93100, 27300 }, summary.Select(s => s.TotalCents).ToArray());
    }

    [Fact]
    public void CompleteWorkflow_NativeFailure_BecomesResultFailureAndCatalogIsUntouched()
    {
        var adapter = new NativeDiscountAdapter((_, _) => throw new DllNotFoundException("simulated"));
        Func<long, int, Result<long>> discount = adapter.Apply;
        var catalog = H.SampleCatalog();

        var result = QuoteWorkflow.Quote(H.Raw("R001", "P001", "2", "2"), catalog, Pricing.Standard, discount);

        Assert.Equal("NATIVE_LIBRARY_NOT_FOUND", H.Error(result).Code);
        Assert.Equal(20, Assert.IsType<Maybe<Product>.Some>(catalog.Find("P001")).Value.Stock);
    }
}

public class DeferredExecutionTests
{
    private static IEnumerable<Result<DeliveryState>> Quoted(IEnumerable<RawRequest> source) =>
        QuoteWorkflow.QuoteAll(source, H.SampleCatalog(), Pricing.Standard, H.Stub);

    [Fact]
    public void QuoteAll_DoesNothingUntilEnumerated_ThenProcessesOnIteration()
    {
        var pulled = 0;
        IEnumerable<RawRequest> Source()
        {
            foreach (var raw in H.SixValid)
            {
                pulled++;
                yield return raw;
            }
        }

        var lazy = Quoted(Source());
        Assert.Equal(0, pulled);

        var snapshot = lazy.ToArray();
        Assert.Equal(6, pulled);
        Assert.Equal(6, snapshot.Length);

        _ = snapshot.Select(r => r).ToArray();
        _ = snapshot.Successes().Count();
        Assert.Equal(6, pulled);

        _ = lazy.ToArray();
        Assert.Equal(12, pulled);
    }

    [Fact]
    public void QuoteAll_EarlyStop_OnlyProcessesWhatWasPulled()
    {
        var pulled = 0;
        IEnumerable<RawRequest> Source()
        {
            foreach (var raw in H.SixValid)
            {
                pulled++;
                yield return raw;
            }
        }

        var first = Quoted(Source()).First();

        Assert.IsType<Result<DeliveryState>.Success>(first);
        Assert.Equal(1, pulled);
    }
}
