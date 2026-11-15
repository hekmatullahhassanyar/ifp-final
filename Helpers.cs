namespace DeliveryQuote.Tests;

internal static class H
{
    public static TValue Value<TValue>(Result<TValue> result) =>
        Assert.IsType<Result<TValue>.Success>(result).Value;

    public static DomainError Error<TValue>(Result<TValue> result) =>
        Assert.IsType<Result<TValue>.Failure>(result).Error;

    public static Catalog SampleCatalog() => Value(Catalog.Create(new[]
    {
        Value(Product.Create("P001", "Office Chair", "Furniture", 8500, 20)),
        Value(Product.Create("P002", "Standing Desk", "Furniture", 24900, 6)),
        Value(Product.Create("P003", "Laptop Stand", "Accessories", 3200, 50)),
        Value(Product.Create("P004", "USB-C Cable", "Accessories", 900, 200)),
        Value(Product.Create("P005", "Monitor 27in", "Electronics", 18900, 4)),
        Value(Product.Create("P006", "Label Printer", "Electronics", 7400, 0)),
    }));

    public static Result<long> StubDiscount(long amount, int percent) =>
        Result.Ok((amount * (100 - percent) + 50) / 100);

    public static readonly Func<long, int, Result<long>> Stub = StubDiscount;

    public static RawRequest Raw(string id, string product, string qty, string km) => new(id, product, qty, km);

    public static PricedQuote Priced(Catalog catalog, ShippingPolicy policy, RawRequest raw)
    {
        var state = Value(QuoteWorkflow.Quote(raw, catalog, policy, Stub));
        return Assert.IsType<DeliveryState.Pending>(state).Quote;
    }

    public static readonly RawRequest[] SixValid =
    {
        Raw("R001", "P001", "2", "2"),
        Raw("R002", "P003", "5", "30"),
        Raw("R003", "P004", "10", "0"),
        Raw("R004", "P005", "3", "120"),
        Raw("R005", "P005", "2", "50"),
        Raw("R006", "P002", "1", "500"),
    };
}

internal sealed class TempDir : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "dq-tests-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Root);

    public string PathOf(string name) => Path.Combine(Root, name);

    public void Dispose()
    {
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }
}
