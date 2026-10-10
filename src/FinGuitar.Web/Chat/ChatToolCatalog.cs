using FinGuitar.Web.Domain;
using Microsoft.Extensions.AI;

namespace FinGuitar.Web.Chat;

/// <summary>The tools, with the roles allowed to use each. Level2 users never get the propose_* tools (FR-C2).</summary>
public class ChatToolCatalog(ChatTools tools)
{
    public record Entry(string Name, string Description, string[] Roles, Delegate Method);

    private static readonly string[] Both = [Roles.Level1, Roles.Level2];
    private static readonly string[] Level1Only = [Roles.Level1];

    public IReadOnlyList<Entry> Entries { get; } =
    [
        new("search_orders", "Find orders by id, customer, product, status, dates or total.", Both, tools.SearchOrders),
        new("get_order_details", "Show one order with its customer and items.", Both, tools.GetOrderDetails),
        new("propose_order_status_change", "Propose a status change for an order. The user must click Confirm.", Level1Only, tools.ProposeOrderStatusChange),
        new("search_products", "Find products by name, brand or category, or list low-stock products.", Both, tools.SearchProducts),
        new("propose_product_update", "Propose a new price and/or stock for a product. The user must click Confirm.", Level1Only, tools.ProposeProductUpdate),
        new("search_customers", "Find customers by name, email or phone.", Both, tools.SearchCustomers),
        new("get_customer_details", "Show one customer with their order history.", Both, tools.GetCustomerDetails),
        new("get_sales_summary", "Order count and revenue for a date range.", Both, tools.GetSalesSummary),
        new("get_top_products", "Best-selling products for a date range.", Both, tools.GetTopProducts),
    ];

    public IReadOnlyList<Entry> ForRole(string role) => Entries.Where(e => e.Roles.Contains(role)).ToList();

    public IList<AITool> BuildFor(string role) =>
        ForRole(role).Select(e => (AITool)AIFunctionFactory.Create(e.Method, e.Name, e.Description)).ToList();
}
