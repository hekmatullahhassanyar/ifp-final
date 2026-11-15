namespace DeliveryQuote.Tests;

public class FileBoundaryTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private string Write(string name, string content)
    {
        var path = _dir.PathOf(name);
        File.WriteAllText(path, content);
        return path;
    }

    private const string Header = "requestId,productId,quantity,distanceKm";

    [Fact]
    public void LoadCatalog_MissingFile_ReturnsFileNotFoundWithPath()
    {
        var error = H.Error(DataStore.LoadCatalog(_dir.PathOf("missing.json")));
        Assert.Equal("FILE_NOT_FOUND", error.Code);
        Assert.Contains("missing.json", error.Message);
    }

    [Theory]
    [InlineData("[{ \"id\": ")]
    [InlineData("{ \"not\": \"a list\" }")]
    [InlineData("")]
    public void LoadCatalog_MalformedContent_ReturnsMalformedData(string content)
    {
        var error = H.Error(DataStore.LoadCatalog(Write("bad.json", content)));
        Assert.Equal("MALFORMED_DATA", error.Code);
    }

    [Fact]
    public void LoadCatalog_ValidFile_LoadsProductsIncludingZeroStock()
    {
        var path = Write("ok.json", """
            [ { "id": "A", "name": "Alpha", "category": "C", "unitPriceCents": 100, "stock": 0 },
              { "id": "B", "name": "Beta",  "category": "C", "unitPriceCents": 250, "stock": 7 } ]
            """);

        var catalog = H.Value(DataStore.LoadCatalog(path));

        Assert.Equal(2, catalog.Products.Count);
        Assert.Equal(0, Assert.IsType<Maybe<Product>.Some>(catalog.Find("A")).Value.Stock);
    }

    [Fact]
    public void LoadCatalog_EmptyList_IsValidEmptyCatalog()
    {
        Assert.Empty(H.Value(DataStore.LoadCatalog(Write("empty.json", "[]"))).Products);
    }

    [Fact]
    public void LoadCatalog_OneInvalidProduct_FailsWholeLoadWithoutPartialCatalog()
    {
        var path = Write("neg.json", """
            [ { "id": "A", "name": "Alpha", "category": "C", "unitPriceCents": 100, "stock": 1 },
              { "id": "B", "name": "Beta",  "category": "C", "unitPriceCents": -5,  "stock": 1 } ]
            """);

        var error = H.Error(DataStore.LoadCatalog(path));

        Assert.Equal("INVALID_PRICE", error.Code);
        Assert.Contains("neg.json", error.Message);
    }

    [Fact]
    public void LoadCatalog_MissingPriceField_IsNotTreatedAsZero()
    {
        var path = Write("noprice.json", """[ { "id": "A", "name": "Alpha", "category": "C", "stock": 1 } ]""");
        Assert.Equal("MISSING_FIELD", H.Error(DataStore.LoadCatalog(path)).Code);
    }

    [Fact]
    public void LoadRequests_MissingFile_ReturnsFileNotFound()
    {
        Assert.Equal("FILE_NOT_FOUND", H.Error(DataStore.LoadRequests(_dir.PathOf("none.csv"))).Code);
    }

    [Theory]
    [InlineData("requestId,productId\nR1,P1")]
    [InlineData(Header + "\nR1,P1,2")]
    [InlineData("")]
    public void LoadRequests_MalformedContent_ReturnsMalformedData(string content)
    {
        Assert.Equal("MALFORMED_DATA", H.Error(DataStore.LoadRequests(Write("bad.csv", content))).Code);
    }

    [Fact]
    public void LoadRequests_ValidFile_ReturnsSnapshotAndKeepsRawTextForLaterValidation()
    {
        var path = Write("ok.csv", Header + "\nR1,P1,2,5\n\nR2,P2,abc,7\n");

        var rows = H.Value(DataStore.LoadRequests(path));

        Assert.Equal(2, rows.Count);
        Assert.Equal("abc", rows[1].Quantity);
    }

    [Fact]
    public void StreamRequests_EarlyStop_ReleasesTheFile()
    {
        var path = Write("early.csv", Header + "\nR1,P1,1,1\nR2,P1,1,1\n");

        using (var e = DataStore.StreamRequests(path).GetEnumerator())
        {
            Assert.True(e.MoveNext());
            Assert.Equal("R1", e.Current.RequestId);
        }

        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(exclusive.CanWrite);
    }

    [Fact]
    public void StreamRequests_FailureDuringIteration_ReleasesTheFile()
    {
        var path = Write("fail.csv", Header + "\nR1,P1,1,1\nBROKEN\n");

        Assert.Throws<FormatException>(() => DataStore.StreamRequests(path).ToArray());

        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(exclusive.CanWrite);
    }

    [Fact]
    public void SaveCatalog_ThenLoad_RoundTripsAndLeavesNoTempFile()
    {
        var updated = H.Value(H.SampleCatalog().Reserve("P005", 3));
        var path = Path.Combine(_dir.PathOf("out"), "saved.json");

        Assert.Equal(path, H.Value(DataStore.SaveCatalog(path, updated)));
        var reloaded = H.Value(DataStore.LoadCatalog(path));

        Assert.Equal(1, Assert.IsType<Maybe<Product>.Some>(reloaded.Find("P005")).Value.Stock);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void ExportLines_WritesReportFile()
    {
        var path = Path.Combine(_dir.PathOf("exports"), "report.txt");

        H.Value(DataStore.ExportLines(path, new[] { "line one", "line two" }));

        Assert.Equal(new[] { "line one", "line two" }, File.ReadAllLines(path));
    }
}

public class NativeIntegrationTests
{
    [Theory]
    [InlineData(995L, 10, 896L)]
    [InlineData(12345L, 5, 11728L)]
    [InlineData(1000L, 0, 1000L)]
    [InlineData(1000L, 100, 0L)]
    [InlineData(0L, 50, 0L)]
    public void RealNativeCall_ReturnsManuallyCalculatedValue(long amount, int percent, long expected)
    {
        var result = new NativeDiscountAdapter().Apply(amount, percent);
        Assert.Equal(expected, H.Value(result));
    }

    [Fact]
    public void Adapter_InvalidDomainInput_IsRejectedBeforeTheNativeCall()
    {
        var calls = 0;
        var adapter = new NativeDiscountAdapter((a, p) => { calls++; return a; });

        Assert.Equal("INVALID_DISCOUNT", H.Error(adapter.Apply(1000, 101)).Code);
        Assert.Equal("INVALID_DISCOUNT", H.Error(adapter.Apply(1000, -1)).Code);
        Assert.Equal("INVALID_AMOUNT", H.Error(adapter.Apply(-1, 10)).Code);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Adapter_LoadingAndBindingFailures_BecomeClearResultFailures()
    {
        Assert.Equal("NATIVE_LIBRARY_NOT_FOUND",
            H.Error(new NativeDiscountAdapter((_, _) => throw new DllNotFoundException("x")).Apply(1, 1)).Code);
        Assert.Equal("NATIVE_ENTRY_POINT_NOT_FOUND",
            H.Error(new NativeDiscountAdapter((_, _) => throw new EntryPointNotFoundException("x")).Apply(1, 1)).Code);
        Assert.Equal("NATIVE_ARCHITECTURE_MISMATCH",
            H.Error(new NativeDiscountAdapter((_, _) => throw new BadImageFormatException("x")).Apply(1, 1)).Code);
    }

    [Theory]
    [InlineData(-1L, "NATIVE_INVALID_ARGUMENT")]
    [InlineData(-2L, "NATIVE_OVERFLOW")]
    [InlineData(-9L, "NATIVE_UNKNOWN_CODE")]
    public void Adapter_NativeErrorCodes_AreTranslated(long nativeCode, string expectedCode)
    {
        var adapter = new NativeDiscountAdapter((_, _) => nativeCode);
        Assert.Equal(expectedCode, H.Error(adapter.Apply(10, 10)).Code);
    }
}
