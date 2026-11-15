namespace DeliveryQuote;

public static class Program
{
    private sealed record Inputs(Catalog Catalog, IReadOnlyList<RawRequest> Requests);

    public static int Main(string[] args)
    {
        var dataDir = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "Data");
        var outDir = args.Length > 1 ? args[1] : Path.Combine(Environment.CurrentDirectory, "output");

        return DataStore.LoadCatalog(Path.Combine(dataDir, "products.json"))
            .Bind(catalog => DataStore.LoadRequests(Path.Combine(dataDir, "requests.csv"))
                .Map(requests => new Inputs(catalog, requests)))
            .Match<Inputs, int>(
                inputs => Run(inputs, dataDir, outDir),
                error =>
                {
                    Console.Error.WriteLine($"[{error.Code}] {error.Message}");
                    return 1;
                });
    }

    private static int Run(Inputs inputs, string dataDir, string outDir)
    {
        var adapter = new NativeDiscountAdapter();
        Func<long, int, Result<long>> discount = adapter.Apply;
        var report = new List<string>();
        DeliveryState[] standardStates = Array.Empty<DeliveryState>();

        foreach (var policy in new[] { Pricing.Standard, Pricing.Express })
        {
            Console.WriteLine($"=== Quotes with {policy.Name} shipping ===");
            var results = QuoteWorkflow.QuoteAll(inputs.Requests, inputs.Catalog, policy, discount).ToArray();

            foreach (var (raw, result) in inputs.Requests.Zip(results))
            {
                Console.WriteLine(result.Match<DeliveryState, string>(
                    Describe,
                    error => $"REJECTED {raw.RequestId}: [{error.Code}] {error.Message}"));
            }

            if (policy.Name == Pricing.Standard.Name)
                standardStates = results.Successes().ToArray();
            Console.WriteLine();
        }

        var quotes = standardStates.OfType<DeliveryState.Pending>().Select(p => p.Quote).ToArray();

        Console.WriteLine("=== Report 1: revenue by category (Standard) ===");
        foreach (var row in Reports.CategorySummaries(quotes))
        {
            var line = $"{row.Category,-12} quotes={row.QuoteCount}  total={row.TotalCents.ToMoney()}";
            Console.WriteLine(line);
            report.Add(line);
        }

        Console.WriteLine();
        Console.WriteLine("=== Report 2: high-value quotes >= 500.00 (Standard) ===");
        foreach (var row in Reports.HighValueQuotes(quotes, 50_000))
        {
            var line = $"{row.RequestId}  {row.ProductName,-14} {row.TotalCents.ToMoney()}";
            Console.WriteLine(line);
            report.Add(line);
        }

        Console.WriteLine();
        Console.WriteLine("=== Confirming Standard quotes in order (stock is reserved) ===");
        var (updatedCatalog, orders) = Fulfilment.ConfirmAll(inputs.Catalog, standardStates);
        foreach (var order in orders)
        {
            Console.WriteLine(order.Match<DeliveryState.Confirmed, string>(
                confirmed => $"CONFIRMED {confirmed.ConfirmationId}",
                error => $"NOT CONFIRMED [{error.Code}] {error.Message}"));
        }

        if (orders.Successes().FirstOrDefault() is { } first)
        {
            Console.WriteLine("Repeat confirmation: " + Fulfilment.Confirm(first, updatedCatalog)
                .Match<ConfirmationOutcome, string>(_ => "unexpectedly accepted", e => $"rejected [{e.Code}]"));
        }

        Console.WriteLine();
        var saved = DataStore.SaveCatalog(Path.Combine(outDir, "products.updated.json"), updatedCatalog);
        var exported = DataStore.ExportLines(Path.Combine(outDir, "report.txt"), report);
        Console.WriteLine(saved.Match<string, string>(p => $"Saved updated inventory: {p}", e => $"[{e.Code}] {e.Message}"));
        Console.WriteLine(exported.Match<string, string>(p => $"Exported report: {p}", e => $"[{e.Code}] {e.Message}"));

        Console.WriteLine();
        Console.WriteLine("Failure demo (missing file): " + DataStore.LoadCatalog(Path.Combine(dataDir, "missing.json"))
            .Match<Catalog, string>(_ => "unexpected success", e => $"[{e.Code}] {e.Message}"));

        return 0;
    }

    private static string Describe(DeliveryState state) => state switch
    {
        DeliveryState.Pending p => FormatQuote(p.Quote),
        DeliveryState.Confirmed c => $"CONFIRMED {c.ConfirmationId}: {FormatQuote(c.Quote)}",
        _ => "UNKNOWN STATE"
    };

    private static string FormatQuote(PricedQuote q) =>
        $"{q.Request.RequestId} {q.Request.Product.Name} x{q.Request.Quantity} over {q.Request.DistanceKm} km: " +
        $"items {q.ItemsCents.ToMoney()} -{q.DiscountPercent}% = {q.DiscountedItemsCents.ToMoney()}, " +
        $"shipping {q.ShippingCents.ToMoney()}, TOTAL {q.TotalCents.ToMoney()}";
}
