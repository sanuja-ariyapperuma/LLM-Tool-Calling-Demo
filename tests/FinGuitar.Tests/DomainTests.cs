using FinGuitar.Web.Domain;

namespace FinGuitar.Tests;

public class OrderStatusRulesTests
{
    private static readonly HashSet<(OrderStatus, OrderStatus)> Allowed =
    [
        (OrderStatus.Pending, OrderStatus.Dispatched),
        (OrderStatus.Dispatched, OrderStatus.Delivered),
        (OrderStatus.Pending, OrderStatus.Cancelled),
    ];

    public static IEnumerable<object[]> AllPairs() =>
        from f in Enum.GetValues<OrderStatus>()
        from t in Enum.GetValues<OrderStatus>()
        select new object[] { f, t };

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void Only_the_three_allowed_changes_pass(OrderStatus from, OrderStatus to)
    {
        var order = new Order { Id = 17, Status = from };
        if (Allowed.Contains((from, to)))
        {
            Assert.True(OrderStatusRules.CanChange(from, to));
            order.ChangeStatus(to);
            Assert.Equal(to, order.Status);
        }
        else
        {
            Assert.False(OrderStatusRules.CanChange(from, to));
            var ex = Assert.Throws<DomainException>(() => order.ChangeStatus(to));
            Assert.Equal($"Order 17 is {from} and cannot be changed to {to}.", ex.Message);
            Assert.Equal(from, order.Status);
        }
    }

    [Fact]
    public void AllowedNext_lists_the_buttons_for_each_status()
    {
        Assert.Equal([OrderStatus.Dispatched, OrderStatus.Cancelled], OrderStatusRules.AllowedNext(OrderStatus.Pending));
        Assert.Equal([OrderStatus.Delivered], OrderStatusRules.AllowedNext(OrderStatus.Dispatched));
        Assert.Empty(OrderStatusRules.AllowedNext(OrderStatus.Delivered));
        Assert.Empty(OrderStatusRules.AllowedNext(OrderStatus.Cancelled));
    }
}

public class ProductRulesTests
{
    [Theory]
    [InlineData(0.01, 0)]
    [InlineData(100000, 10000)]
    [InlineData(1, 1)]
    public void Boundary_values_inside_the_range_pass(double price, int stock) =>
        ProductRules.Validate((decimal)price, stock);

    [Theory]
    [InlineData(0, 5, "Price")]
    [InlineData(100000.01, 5, "Price")]
    [InlineData(10, -1, "Stock")]
    [InlineData(10, 10001, "Stock")]
    public void Values_outside_the_range_name_the_bad_field(double price, int stock, string field)
    {
        var ex = Assert.Throws<ValidationException>(() => ProductRules.Validate((decimal)price, stock));
        Assert.Contains(field, ex.Errors.Keys);
        Assert.Single(ex.Errors);
    }

    [Fact]
    public void Both_fields_can_fail_together()
    {
        var ex = Assert.Throws<ValidationException>(() => ProductRules.Validate(0, -5));
        Assert.Equal(2, ex.Errors.Count);
    }
}
