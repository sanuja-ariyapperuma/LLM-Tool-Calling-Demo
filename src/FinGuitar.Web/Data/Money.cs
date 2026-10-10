namespace FinGuitar.Web.Data;

/// <summary>Money is stored as whole cents; services convert at their boundary.</summary>
public static class Money
{
    public static decimal FromCents(long cents) => cents / 100m;
    public static long ToCents(decimal amount) => (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
}
