using FinGuitar.Web.Domain;

namespace FinGuitar.Web.Services;

public record PagedResult<T>(IReadOnlyList<T> Rows, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

public record OrderFilter(
    int? OrderId = null,
    string? CustomerName = null,
    string? ProductName = null,
    OrderStatus? Status = null,
    DateTime? DateFrom = null,
    DateTime? DateTo = null,
    decimal? MinTotal = null,
    decimal? MaxTotal = null);

public record ProductFilter(string? Text = null, bool LowStockOnly = false);

public record AuditFilter(
    DateTime? DateFrom = null,
    DateTime? DateTo = null,
    ChangeSource? Source = null,
    int? UserId = null,
    string? EntityType = null,
    int? EntityId = null);
