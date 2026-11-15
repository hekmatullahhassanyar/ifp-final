namespace DeliveryQuote.Tests;

public class ResultTests
{
    [Fact]
    public void Map_OnSuccess_TransformsValue()
    {
        var result = Result.Ok(2).Map(x => x * 10);
        Assert.Equal(20, H.Value(result));
    }

    [Fact]
    public void Map_OnFailure_PreservesFailureAndDoesNotCallFunction()
    {
        var calls = 0;
        var result = Result.Fail<int>("E1", "boom").Map(x => { calls++; return x; });

        Assert.Equal(0, calls);
        Assert.Equal("E1", H.Error(result).Code);
    }

    [Fact]
    public void Bind_OnSuccess_PassesValueAndPreservesInnerResult()
    {
        var ok = Result.Ok(2).Bind(x => Result.Ok(x.ToString()));
        var inner = Result.Ok(2).Bind(x => Result.Fail<string>("INNER", "inner failed"));

        Assert.Equal("2", H.Value(ok));
        Assert.Equal("INNER", H.Error(inner).Code);
    }

    [Fact]
    public void Bind_OnFailure_PreservesFailureAndDoesNotCallFunction()
    {
        var calls = 0;
        var result = Result.Fail<int>("E2", "boom").Bind(x => { calls++; return Result.Ok(x); });

        Assert.Equal(0, calls);
        Assert.Equal("E2", H.Error(result).Code);
    }

    [Fact]
    public void Maybe_ToResult_PresentAndAbsent_AndZeroIsNotAbsence()
    {
        var missing = new DomainError("MISSING", "not there");

        Assert.Equal(0, H.Value(Maybe.Some(0).ToResult(missing)));
        Assert.Equal("", H.Value(Maybe.Some("").ToResult(missing)));
        Assert.Equal("MISSING", H.Error(Maybe.None<int>().ToResult(missing)).Code);
    }
}

public class ProductAndCatalogTests
{
    [Fact]
    public void Product_Create_ValidInput_ReturnsProduct()
    {
        var p = H.Value(Product.Create(" P9 ", " Lamp ", " Home ", 1500, 3));
        Assert.Equal("P9", p.Id);
        Assert.Equal("Lamp", p.Name);
        Assert.Equal("Home", p.Category);
        Assert.Equal(1500, p.UnitPriceCents);
        Assert.Equal(3, p.Stock);
    }

    [Theory]
    [InlineData(" ", "n", "c", 100L, 1, "INVALID_ID")]
    [InlineData("id", "", "c", 100L, 1, "INVALID_NAME")]
    [InlineData("id", "n", null, 100L, 1, "INVALID_CATEGORY")]
    [InlineData("id", "n", "c", 0L, 1, "INVALID_PRICE")]
    [InlineData("id", "n", "c", -5L, 1, "INVALID_PRICE")]
    [InlineData("id", "n", "c", 100L, -1, "INVALID_STOCK")]
    public void Product_Create_InvalidInput_ReportsRule(string? id, string? name, string? category, long price, int stock, string code)
    {
        Assert.Equal(code, H.Error(Product.Create(id, name, category, price, stock)).Code);
    }

    [Fact]
    public void Product_Create_ZeroStock_IsValidBoundary()
    {
        Assert.Equal(0, H.Value(Product.Create("id", "n", "c", 100, 0)).Stock);
    }

    [Fact]
    public void Catalog_Find_PresentAndAbsent()
    {
        var catalog = H.SampleCatalog();
        var found = Assert.IsType<Maybe<Product>.Some>(catalog.Find("P001"));
        Assert.Equal("Office Chair", found.Value.Name);
        Assert.IsType<Maybe<Product>.None>(catalog.Find("NOPE"));
    }

    [Fact]
    public void Catalog_Create_DuplicateIds_Fails()
    {
        var a = H.Value(Product.Create("X", "A", "C", 100, 1));
        var b = H.Value(Product.Create("X", "B", "C", 100, 1));
        Assert.Equal("DUPLICATE_PRODUCT", H.Error(Catalog.Create(new[] { a, b })).Code);
    }

    [Fact]
    public void Catalog_Create_EmptyList_IsValidEmptyCatalog()
    {
        Assert.Empty(H.Value(Catalog.Create(Array.Empty<Product>())).Products);
    }

    [Fact]
    public void Catalog_Reserve_Success_ReturnsNewCatalogAndLeavesOriginalUnchanged()
    {
        var original = H.SampleCatalog();
        var updated = H.Value(original.Reserve("P005", 3));

        Assert.Equal(1, Assert.IsType<Maybe<Product>.Some>(updated.Find("P005")).Value.Stock);
        Assert.Equal(4, Assert.IsType<Maybe<Product>.Some>(original.Find("P005")).Value.Stock);
    }

    [Fact]
    public void Catalog_Reserve_Failures_ReportReasonAndLeaveStateUnchanged()
    {
        var catalog = H.SampleCatalog();

        Assert.Equal("INSUFFICIENT_STOCK", H.Error(catalog.Reserve("P005", 5)).Code);
        Assert.Equal("PRODUCT_NOT_FOUND", H.Error(catalog.Reserve("NOPE", 1)).Code);
        Assert.Equal("INVALID_QUANTITY", H.Error(catalog.Reserve("P005", 0)).Code);
        Assert.Equal(4, Assert.IsType<Maybe<Product>.Some>(catalog.Find("P005")).Value.Stock);
    }

    [Fact]
    public void Catalog_Products_AreOrderedById()
    {
        var ids = H.SampleCatalog().Products.Select(p => p.Id).ToArray();
        Assert.Equal(new[] { "P001", "P002", "P003", "P004", "P005", "P006" }, ids);
    }
}

public class RequestValidationTests
{
    [Theory]
    [InlineData("0", "10", "INVALID_QUANTITY")]
    [InlineData("-1", "10", "INVALID_QUANTITY")]
    [InlineData("abc", "10", "INVALID_QUANTITY")]
    [InlineData("", "10", "INVALID_QUANTITY")]
    [InlineData("1", "-1", "INVALID_DISTANCE")]
    [InlineData("1", "xyz", "INVALID_DISTANCE")]
    [InlineData("1", "501", "OUT_OF_SERVICE_AREA")]
    public void Create_InvalidFields_ReportsTheBrokenRule(string quantity, string distance, string code)
    {
        var result = ValidatedRequest.Create(H.Raw("R1", "P001", quantity, distance), H.SampleCatalog());
        Assert.Equal(code, H.Error(result).Code);
    }

    [Fact]
    public void Create_UnknownProduct_ReportsMissingLookup()
    {
        var error = H.Error(ValidatedRequest.Create(H.Raw("R1", "P999", "1", "1"), H.SampleCatalog()));
        Assert.Equal("PRODUCT_NOT_FOUND", error.Code);
        Assert.Contains("P999", error.Message);
    }

    [Fact]
    public void Create_StockBoundary_ExactStockAccepted_OneMoreRejected()
    {
        var catalog = H.SampleCatalog();
        Assert.Equal(4, H.Value(ValidatedRequest.Create(H.Raw("R1", "P005", "4", "1"), catalog)).Quantity);
        Assert.Equal("INSUFFICIENT_STOCK", H.Error(ValidatedRequest.Create(H.Raw("R1", "P005", "5", "1"), catalog)).Code);
        Assert.Equal("INSUFFICIENT_STOCK", H.Error(ValidatedRequest.Create(H.Raw("R1", "P006", "1", "1"), catalog)).Code);
    }

    [Fact]
    public void Create_DistanceBoundaries_ZeroAndMaxAccepted()
    {
        var catalog = H.SampleCatalog();
        Assert.Equal(0, H.Value(ValidatedRequest.Create(H.Raw("R1", "P001", "1", "0"), catalog)).DistanceKm);
        Assert.Equal(500, H.Value(ValidatedRequest.Create(H.Raw("R1", "P001", "1", "500"), catalog)).DistanceKm);
    }

    [Fact]
    public void Create_NullOrBlankInput_IsRejectedNotFabricated()
    {
        var catalog = H.SampleCatalog();
        Assert.Equal("MISSING_INPUT", H.Error(ValidatedRequest.Create(null, catalog)).Code);
        Assert.Equal("INVALID_REQUEST_ID", H.Error(ValidatedRequest.Create(H.Raw(" ", "P001", "1", "1"), catalog)).Code);
        Assert.Equal("INVALID_QUANTITY", H.Error(ValidatedRequest.Create(new RawRequest("R1", "P001", null, "1"), catalog)).Code);
    }
}

public class PricingTests
{
    [Fact]
    public void TwoPartiallyAppliedPolicies_SameRequest_GiveDifferentShipping()
    {
        var request = H.Value(ValidatedRequest.Create(H.Raw("R1", "P001", "1", "2"), H.SampleCatalog()));

        Assert.Equal(700, Pricing.Standard.Calculate(request));
        Assert.Equal(1100, Pricing.Express.Calculate(request));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(4, 0)]
    [InlineData(5, 5)]
    [InlineData(9, 5)]
    [InlineData(10, 10)]
    [InlineData(250, 10)]
    public void DiscountTiers_FollowBoundaries(int quantity, int expectedPercent)
    {
        Assert.Equal(expectedPercent, Pricing.DiscountPercentFor(quantity));
    }

    [Fact]
    public void Quote_UsesInjectedDiscountFunction()
    {
        var catalog = H.SampleCatalog();
        var request = H.Value(ValidatedRequest.Create(H.Raw("R002", "P003", "5", "30"), catalog));
        var seen = new List<(long Amount, int Percent)>();

        var quote = H.Value(Pricing.Quote(request, Pricing.Standard, (amount, percent) =>
        {
            seen.Add((amount, percent));
            return Result.Ok(amount - 1);
        }));

        Assert.Equal((16000L, 5), Assert.Single(seen));
        Assert.Equal(15999, quote.DiscountedItemsCents);
        Assert.Equal(15999 + 3500, quote.TotalCents);
    }

    [Fact]
    public void Quote_WhenDiscountFunctionFails_FailureIsPropagated()
    {
        var request = H.Value(ValidatedRequest.Create(H.Raw("R1", "P001", "1", "1"), H.SampleCatalog()));
        var result = Pricing.Quote(request, Pricing.Standard, (_, _) => Result.Fail<long>("NATIVE_X", "down"));
        Assert.Equal("NATIVE_X", H.Error(result).Code);
    }
}

public class ExtensionTests
{
    [Theory]
    [InlineData(12345L, "123.45")]
    [InlineData(5L, "0.05")]
    [InlineData(0L, "0.00")]
    [InlineData(-250L, "-2.50")]
    public void ToMoney_FormatsCents(long cents, string expected) => Assert.Equal(expected, cents.ToMoney());

    [Fact]
    public void HighValue_IsInclusiveAtThreshold()
    {
        var catalog = H.SampleCatalog();
        var quotes = H.SixValid.Select(r => H.Priced(catalog, Pricing.Standard, r)).ToArray();

        var ids = quotes.HighValue(75400).Select(q => q.Request.RequestId).ToArray();
        Assert.Equal(new[] { "R006" }, ids);
    }

    [Fact]
    public void Successes_KeepsOnlySuccessfulValuesInOrder()
    {
        var results = new[] { Result.Ok(1), Result.Fail<int>("E", "x"), Result.Ok(3) };
        Assert.Equal(new[] { 1, 3 }, results.Successes().ToArray());
        Assert.Empty(Array.Empty<Result<int>>().Successes());
    }
}
