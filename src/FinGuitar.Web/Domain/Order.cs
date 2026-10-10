namespace FinGuitar.Web.Domain;

public enum OrderStatus { Pending, Dispatched, Delivered, Cancelled }

public class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public DateTime OrderDate { get; set; }
    public OrderStatus Status { get; set; }
    public long TotalCents { get; set; }
    public decimal Total => Data.Money.FromCents(TotalCents);
    public List<OrderLine> Lines { get; set; } = [];

    /// <summary>Changes the status if the rules allow it (FR-D1), otherwise throws a DomainException.</summary>
    public void ChangeStatus(OrderStatus to)
    {
        if (!OrderStatusRules.CanChange(Status, to))
            throw new DomainException($"Order {Id} is {Status} and cannot be changed to {to}.");
        Status = to;
    }
}

public class OrderLine
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public int Quantity { get; set; }
    public long UnitPriceCents { get; set; }
    public decimal UnitPrice => Data.Money.FromCents(UnitPriceCents);
    public decimal LineTotal => UnitPrice * Quantity;
}

public static class OrderStatusRules
{
    private static readonly Dictionary<OrderStatus, OrderStatus[]> Allowed = new()
    {
        [OrderStatus.Pending] = [OrderStatus.Dispatched, OrderStatus.Cancelled],
        [OrderStatus.Dispatched] = [OrderStatus.Delivered],
        [OrderStatus.Delivered] = [],
        [OrderStatus.Cancelled] = [],
    };

    public static bool CanChange(OrderStatus from, OrderStatus to) => Allowed[from].Contains(to);

    public static IReadOnlyList<OrderStatus> AllowedNext(OrderStatus from) => Allowed[from];
}
