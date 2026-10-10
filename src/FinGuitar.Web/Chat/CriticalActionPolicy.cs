using System.Globalization;
using FinGuitar.Web.Domain;

namespace FinGuitar.Web.Chat;

/// <summary>The arguments saved with an order status proposal.</summary>
public record OrderStatusArgs(int OrderId, OrderStatus From, OrderStatus To);

/// <summary>The arguments saved with a product proposal, including the values the card showed (for the stale-card check).</summary>
public record ProductUpdateArgs(int ProductId, string Name, decimal OldPrice, int OldStock, decimal? NewPrice, int? NewStock);

public record CriticalVerdict(bool IsCritical, string? Warning = null, string? DangerLabel = null, string? KeepLabel = null);

/// <summary>
/// The only place the critical-action rules live (D16, FR-C15): cancelling an order, a price change of 20% or more,
/// and setting stock to 0. The server decides this when the proposal is created, never the model.
/// </summary>
public static class CriticalActionPolicy
{
    public const decimal PriceChangeThreshold = 0.20m;

    public static CriticalVerdict Evaluate(OrderStatusArgs a) =>
        a.To == OrderStatus.Cancelled
            ? new CriticalVerdict(true,
                $"Cancelling order {a.OrderId} cannot be undone. The customer's order will be closed.",
                $"Yes, cancel order {a.OrderId}", "Keep order")
            : new CriticalVerdict(false);

    public static CriticalVerdict Evaluate(ProductUpdateArgs a)
    {
        if (a.NewStock == 0)
            return new CriticalVerdict(true,
                $"Setting the stock of {a.Name} to 0 marks it as out of stock, and customers cannot buy it.",
                "Yes, set stock to 0", "Keep current value");

        if (a.NewPrice is { } price && a.OldPrice > 0 && Math.Abs(price - a.OldPrice) / a.OldPrice >= PriceChangeThreshold)
        {
            var pct = (price - a.OldPrice) / a.OldPrice * 100m;
            return new CriticalVerdict(true,
                $"Changing the price of {a.Name} from {Money(a.OldPrice)} to {Money(price)} ({pct.ToString("+0.#;-0.#", CultureInfo.InvariantCulture)}%) applies to every new order.",
                "Yes, change the price", "Keep current value");
        }

        return new CriticalVerdict(false);
    }

    private static string Money(decimal value) => value.ToString("C", CultureInfo.GetCultureInfo("en-US"));
}
