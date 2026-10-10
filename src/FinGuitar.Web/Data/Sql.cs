namespace FinGuitar.Web.Data;

public static class Sql
{
    /// <summary>Builds a "contains" LIKE pattern for use with <c>LIKE @p ESCAPE '\'</c>, escaping %, _ and \.</summary>
    public static string Contains(string term) =>
        "%" + term.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    public static int Offset(int page, int pageSize) => (Math.Max(1, page) - 1) * pageSize;
}
