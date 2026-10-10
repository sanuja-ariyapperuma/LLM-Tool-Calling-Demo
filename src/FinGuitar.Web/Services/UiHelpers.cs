using System.Globalization;
using FinGuitar.Web.Domain;

namespace FinGuitar.Web.Services;

/// <summary>
/// Small formatting helpers for pages. Class names are always written in full (never built from parts),
/// because Tailwind finds classes by scanning source files (spec 2.7.1).
/// </summary>
public static class UiHelpers
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    public static string StatusBadge(OrderStatus status) => status switch
    {
        OrderStatus.Pending => "badge badge-pending",
        OrderStatus.Dispatched => "badge badge-dispatched",
        OrderStatus.Delivered => "badge badge-delivered",
        OrderStatus.Cancelled => "badge badge-cancelled",
        _ => "badge badge-neutral",
    };

    public static string Money(decimal value) => value.ToString("C", Us);

    public static string Date(DateTime value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Stamp(DateTime value) => value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    public static string SourceLabel(ChangeSource source) => source == ChangeSource.Chat ? "Chat" : "Admin panel";
}
