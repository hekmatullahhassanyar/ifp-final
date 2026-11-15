namespace DeliveryQuote;

public sealed record ShippingPolicy(string Name, Func<ValidatedRequest, long> Calculate);

public static class Pricing
{
    public static readonly Func<long, Func<long, Func<ValidatedRequest, long>>> CreateShipping =
        baseCents => perKmCents => request => baseCents + perKmCents * request.DistanceKm;

    public static readonly ShippingPolicy Standard = new("Standard", CreateShipping(500)(100));
    public static readonly ShippingPolicy Express = new("Express", CreateShipping(800)(150));

    public static int DiscountPercentFor(int quantity) => quantity switch
    {
        >= 10 => 10,
        >= 5 => 5,
        _ => 0
    };

    public static Result<PricedQuote> Quote(
        ValidatedRequest request,
        ShippingPolicy policy,
        Func<long, int, Result<long>> applyDiscount)
    {
        var itemsCents = request.Product.UnitPriceCents * request.Quantity;
        var percent = DiscountPercentFor(request.Quantity);

        return applyDiscount(itemsCents, percent).Map(discounted =>
        {
            var shipping = policy.Calculate(request);
            return new PricedQuote(request, policy.Name, itemsCents, percent, discounted, shipping, discounted + shipping);
        });
    }
}
