namespace DeliveryQuote;

public static class QueryExtensions
{
    public static IEnumerable<PricedQuote> HighValue(this IEnumerable<PricedQuote> quotes, long minTotalCents) =>
        quotes.Where(q => q.TotalCents >= minTotalCents);

    public static IEnumerable<T> Successes<T>(this IEnumerable<Result<T>> results)
    {
        foreach (var result in results)
        {
            if (result is Result<T>.Success success)
                yield return success.Value;
        }
    }

    public static string ToMoney(this long cents)
    {
        var abs = Math.Abs(cents);
        return (cents < 0 ? "-" : "") + (abs / 100) + "." + (abs % 100).ToString("D2");
    }
}
