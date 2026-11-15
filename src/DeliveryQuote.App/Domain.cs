using System.Collections.Immutable;
using System.Globalization;

namespace DeliveryQuote;

public sealed record RawRequest(string? RequestId, string? ProductId, string? Quantity, string? DistanceKm);

public sealed record Product
{
    public string Id { get; }
    public string Name { get; }
    public string Category { get; }
    public long UnitPriceCents { get; }
    public int Stock { get; }

    private Product(string id, string name, string category, long unitPriceCents, int stock)
    {
        Id = id;
        Name = name;
        Category = category;
        UnitPriceCents = unitPriceCents;
        Stock = stock;
    }

    public static Result<Product> Create(string? id, string? name, string? category, long unitPriceCents, int stock)
    {
        if (string.IsNullOrWhiteSpace(id))
            return Result.Fail<Product>("INVALID_ID", "Product id must not be blank.");
        if (string.IsNullOrWhiteSpace(name))
            return Result.Fail<Product>("INVALID_NAME", $"Product {id}: name must not be blank.");
        if (string.IsNullOrWhiteSpace(category))
            return Result.Fail<Product>("INVALID_CATEGORY", $"Product {id}: category must not be blank.");
        if (unitPriceCents <= 0)
            return Result.Fail<Product>("INVALID_PRICE", $"Product {id}: unit price must be positive (was {unitPriceCents}).");
        if (stock < 0)
            return Result.Fail<Product>("INVALID_STOCK", $"Product {id}: stock must not be negative (was {stock}).");

        return Result.Ok(new Product(id.Trim(), name.Trim(), category.Trim(), unitPriceCents, stock));
    }

    public Result<Product> ReduceStock(int quantity)
    {
        if (quantity <= 0)
            return Result.Fail<Product>("INVALID_QUANTITY", $"Product {Id}: quantity to reserve must be positive.");
        if (quantity > Stock)
            return Result.Fail<Product>("INSUFFICIENT_STOCK", $"Product {Id}: requested {quantity} but only {Stock} in stock.");

        return Result.Ok(new Product(Id, Name, Category, UnitPriceCents, Stock - quantity));
    }
}

public sealed class Catalog
{
    private readonly ImmutableDictionary<string, Product> _items;

    private Catalog(ImmutableDictionary<string, Product> items) => _items = items;

    public static Result<Catalog> Create(IEnumerable<Product> products)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, Product>(StringComparer.Ordinal);
        foreach (var product in products)
        {
            if (builder.ContainsKey(product.Id))
                return Result.Fail<Catalog>("DUPLICATE_PRODUCT", $"Product id '{product.Id}' appears more than once.");
            builder.Add(product.Id, product);
        }
        return Result.Ok(new Catalog(builder.ToImmutable()));
    }

    public IReadOnlyList<Product> Products =>
        _items.Values.OrderBy(p => p.Id, StringComparer.Ordinal).ToArray();

    public Maybe<Product> Find(string productId) =>
        _items.TryGetValue(productId, out var product) ? Maybe.Some(product) : Maybe.None<Product>();

    public Result<Catalog> Reserve(string productId, int quantity) => Find(productId) switch
    {
        Maybe<Product>.Some found => found.Value.ReduceStock(quantity)
            .Map(updated => new Catalog(_items.SetItem(productId, updated))),
        Maybe<Product>.None => Result.Fail<Catalog>("PRODUCT_NOT_FOUND", $"Product '{productId}' does not exist."),
        _ => throw new InvalidOperationException("Unknown Maybe case.")
    };
}

public sealed record ValidatedRequest
{
    public const int MaxDistanceKm = 500;

    public string RequestId { get; }
    public Product Product { get; }
    public int Quantity { get; }
    public int DistanceKm { get; }

    private ValidatedRequest(string requestId, Product product, int quantity, int distanceKm)
    {
        RequestId = requestId;
        Product = product;
        Quantity = quantity;
        DistanceKm = distanceKm;
    }

    public static Result<ValidatedRequest> Create(RawRequest? raw, Catalog catalog)
    {
        if (raw is null)
            return Result.Fail<ValidatedRequest>("MISSING_INPUT", "Request is missing.");
        if (string.IsNullOrWhiteSpace(raw.RequestId))
            return Result.Fail<ValidatedRequest>("INVALID_REQUEST_ID", "Request id must not be blank.");

        var requestId = raw.RequestId.Trim();

        if (!TryParseInt(raw.Quantity, out var quantity) || quantity <= 0)
            return Result.Fail<ValidatedRequest>("INVALID_QUANTITY",
                $"Request {requestId}: quantity '{raw.Quantity}' must be a whole number greater than 0.");
        if (!TryParseInt(raw.DistanceKm, out var distanceKm) || distanceKm < 0)
            return Result.Fail<ValidatedRequest>("INVALID_DISTANCE",
                $"Request {requestId}: distance '{raw.DistanceKm}' must be a whole number of km, 0 or more.");
        if (distanceKm > MaxDistanceKm)
            return Result.Fail<ValidatedRequest>("OUT_OF_SERVICE_AREA",
                $"Request {requestId}: {distanceKm} km exceeds the {MaxDistanceKm} km service limit.");

        var productId = (raw.ProductId ?? string.Empty).Trim();
        var missing = new DomainError("PRODUCT_NOT_FOUND", $"Request {requestId}: product '{productId}' does not exist.");

        return catalog.Find(productId)
            .ToResult(missing)
            .Bind(product => quantity > product.Stock
                ? Result.Fail<ValidatedRequest>("INSUFFICIENT_STOCK",
                    $"Request {requestId}: requested {quantity} of {product.Id} but only {product.Stock} in stock.")
                : Result.Ok(new ValidatedRequest(requestId, product, quantity, distanceKm)));
    }

    private static bool TryParseInt(string? text, out int value)
    {
        value = 0;
        return text is not null &&
               int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}

public sealed record PricedQuote(
    ValidatedRequest Request,
    string PolicyName,
    long ItemsCents,
    int DiscountPercent,
    long DiscountedItemsCents,
    long ShippingCents,
    long TotalCents);

public abstract record DeliveryState
{
    private DeliveryState() { }

    public sealed record Pending(PricedQuote Quote) : DeliveryState;
    public sealed record Confirmed(PricedQuote Quote, string ConfirmationId) : DeliveryState;
}

public sealed record ConfirmationOutcome(Catalog Catalog, DeliveryState.Confirmed Order);
