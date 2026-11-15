namespace DeliveryQuote;

public static class QuoteWorkflow
{

    public static Result<ValidatedRequest> Stage1Validate(RawRequest raw, Catalog catalog) =>
        ValidatedRequest.Create(raw, catalog);

    public static Result<PricedQuote> Stage2Price(
        ValidatedRequest request, ShippingPolicy policy, Func<long, int, Result<long>> applyDiscount) =>
        Pricing.Quote(request, policy, applyDiscount);

    public static DeliveryState Stage3OpenPending(PricedQuote quote) => new DeliveryState.Pending(quote);

    public static Result<DeliveryState> Quote(
        RawRequest raw, Catalog catalog, ShippingPolicy policy, Func<long, int, Result<long>> applyDiscount) =>
        Stage1Validate(raw, catalog)
            .Bind(request => Stage2Price(request, policy, applyDiscount))
            .Map<PricedQuote, DeliveryState>(Stage3OpenPending);

    public static IEnumerable<Result<DeliveryState>> QuoteAll(
        IEnumerable<RawRequest> requests, Catalog catalog, ShippingPolicy policy,
        Func<long, int, Result<long>> applyDiscount)
    {
        foreach (var raw in requests)
            yield return Quote(raw, catalog, policy, applyDiscount);
    }
}

public static class Fulfilment
{
    public static Result<ConfirmationOutcome> Confirm(DeliveryState state, Catalog catalog) => state switch
    {
        DeliveryState.Pending pending =>
            catalog.Reserve(pending.Quote.Request.Product.Id, pending.Quote.Request.Quantity)
                .Map(updated => new ConfirmationOutcome(
                    updated,
                    new DeliveryState.Confirmed(pending.Quote, "CONF-" + pending.Quote.Request.RequestId))),
        DeliveryState.Confirmed done =>
            Result.Fail<ConfirmationOutcome>("ALREADY_CONFIRMED",
                $"Request {done.Quote.Request.RequestId} is already confirmed as {done.ConfirmationId}."),
        _ => Result.Fail<ConfirmationOutcome>("UNKNOWN_STATE", "Unrecognised delivery state.")
    };

    public static (Catalog Catalog, IReadOnlyList<Result<DeliveryState.Confirmed>> Orders) ConfirmAll(
        Catalog catalog, IEnumerable<DeliveryState> states)
    {
        var current = catalog;
        var orders = new List<Result<DeliveryState.Confirmed>>();

        foreach (var state in states)
        {
            var outcome = Confirm(state, current);
            if (outcome is Result<ConfirmationOutcome>.Success success)
                current = success.Value.Catalog;
            orders.Add(outcome.Map(o => o.Order));
        }
        return (current, orders);
    }
}
