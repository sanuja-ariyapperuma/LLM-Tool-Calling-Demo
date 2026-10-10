using System.Text.Json;
using FinGuitar.Web.Domain;
using FinGuitar.Web.Services;

namespace FinGuitar.Web.Chat;

// What the server draws under an assistant message. Built from tool results, never from model text (FR-C3).

public record OrderTableRow(int Id, string Customer, string Date, string Status, decimal Total, string Items);
public record OrderTablePayload(IReadOnlyList<OrderTableRow> Rows, int TotalCount);

public record OrderLinePayload(int ProductId, string Product, int Quantity, decimal UnitPrice);
public record OrderDetailPayload(int Id, int CustomerId, string Customer, string Date, string Status, decimal Total,
    IReadOnlyList<OrderLinePayload> Lines);

public record ProductTableRow(int Id, string Name, string Brand, string Category, decimal Price, int Stock);
public record ProductTablePayload(IReadOnlyList<ProductTableRow> Rows, int TotalCount);

public record CustomerTableRow(int Id, string Name, string Email, string Phone, int OrderCount);
public record CustomerTablePayload(IReadOnlyList<CustomerTableRow> Rows, int TotalCount);

public record CustomerDetailPayload(int Id, string Name, string Email, string Phone, string Address,
    IReadOnlyList<OrderTableRow> Orders);

public record SalesSummaryPayload(string From, string To, int OrderCount, decimal Revenue);

public record TopProductRow(int ProductId, string Name, int Units, decimal Revenue);
public record TopProductsPayload(string From, string To, IReadOnlyList<TopProductRow> Rows);

/// <summary>Only the id and text are stored: the card's live status is read from proposed_actions (FR-C11).</summary>
public record ProposalCardPayload(Guid ActionId, string Summary, bool IsCritical);

public record FinalConfirmCardPayload(Guid ActionId, string Summary, string Warning, string DangerLabel, string KeepLabel);

public record ChatPayload(string Kind, object Data);

public static class PayloadKinds
{
    public const string OrderTable = nameof(OrderTable);
    public const string OrderDetail = nameof(OrderDetail);
    public const string ProductTable = nameof(ProductTable);
    public const string CustomerTable = nameof(CustomerTable);
    public const string CustomerDetail = nameof(CustomerDetail);
    public const string SalesSummary = nameof(SalesSummary);
    public const string TopProducts = nameof(TopProducts);
    public const string ProposalCard = nameof(ProposalCard);
    public const string FinalConfirmCard = nameof(FinalConfirmCard);
}

public static class ChatPayloadJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(object data) => JsonSerializer.Serialize(data, Options);

    public static object? Deserialize(string? kind, string? json)
    {
        if (string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(json)) return null;
        Type? type = kind switch
        {
            PayloadKinds.OrderTable => typeof(OrderTablePayload),
            PayloadKinds.OrderDetail => typeof(OrderDetailPayload),
            PayloadKinds.ProductTable => typeof(ProductTablePayload),
            PayloadKinds.CustomerTable => typeof(CustomerTablePayload),
            PayloadKinds.CustomerDetail => typeof(CustomerDetailPayload),
            PayloadKinds.SalesSummary => typeof(SalesSummaryPayload),
            PayloadKinds.TopProducts => typeof(TopProductsPayload),
            PayloadKinds.ProposalCard => typeof(ProposalCardPayload),
            PayloadKinds.FinalConfirmCard => typeof(FinalConfirmCardPayload),
            _ => null,
        };
        return type is null ? null : JsonSerializer.Deserialize(json, type, Options);
    }

    /// <summary>One line the model sees in history instead of the table, so "the second one" can be resolved (2.6.3).</summary>
    public static string? Summarise(string? kind, string? json) => Deserialize(kind, json) switch
    {
        OrderTablePayload t => $"[Shown: orders {string.Join(", ", t.Rows.Select(r => r.Id))}" + More(t.TotalCount, t.Rows.Count) + "]",
        OrderDetailPayload d => $"[Shown: order {d.Id}]",
        ProductTablePayload t => $"[Shown: products {string.Join(", ", t.Rows.Select(r => r.Id))}" + More(t.TotalCount, t.Rows.Count) + "]",
        CustomerTablePayload t => $"[Shown: customers {string.Join(", ", t.Rows.Select(r => r.Id))}" + More(t.TotalCount, t.Rows.Count) + "]",
        CustomerDetailPayload c => $"[Shown: customer {c.Id}]",
        SalesSummaryPayload s => $"[Shown: sales {s.From} to {s.To}]",
        TopProductsPayload t => $"[Shown: top products {t.From} to {t.To}]",
        ProposalCardPayload p => $"[Shown: confirmation card: {p.Summary}]",
        FinalConfirmCardPayload f => $"[Shown: final confirmation card: {f.Summary}]",
        _ => null,
    };

    private static string More(int total, int shown) => total > shown ? $" (of {total})" : "";
}
