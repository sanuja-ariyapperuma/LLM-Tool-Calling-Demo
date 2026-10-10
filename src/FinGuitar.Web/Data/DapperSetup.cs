using System.Data;
using System.Globalization;
using Dapper;

namespace FinGuitar.Web.Data;

/// <summary>All Dapper configuration in one place: snake_case mapping and the TEXT formats for dates and Guids.</summary>
public static class DapperSetup
{
    private const string DateFormat = "yyyy-MM-dd HH:mm:ss";
    private static readonly object Gate = new();
    private static bool _done;

    public static string FormatDate(DateTime value) => value.ToString(DateFormat, CultureInfo.InvariantCulture);

    public static void Configure()
    {
        lock (Gate)
        {
            if (_done) return;
            DefaultTypeMap.MatchNamesWithUnderscores = true;
            // Dapper's built-in mappings win over handlers for parameters, so remove them first.
            SqlMapper.RemoveTypeMap(typeof(DateTime));
            SqlMapper.RemoveTypeMap(typeof(DateTime?));
            SqlMapper.RemoveTypeMap(typeof(Guid));
            SqlMapper.RemoveTypeMap(typeof(Guid?));
            SqlMapper.AddTypeHandler(new DateTimeHandler());
            SqlMapper.AddTypeHandler(new GuidHandler());
            _done = true;
        }
    }

    private sealed class DateTimeHandler : SqlMapper.TypeHandler<DateTime>
    {
        public override void SetValue(IDbDataParameter parameter, DateTime value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = FormatDate(value);
        }

        public override DateTime Parse(object value) => value is DateTime d
            ? d
            : DateTime.Parse((string)value, CultureInfo.InvariantCulture);
    }

    private sealed class GuidHandler : SqlMapper.TypeHandler<Guid>
    {
        public override void SetValue(IDbDataParameter parameter, Guid value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = value.ToString();
        }

        public override Guid Parse(object value) => value is Guid g ? g : Guid.Parse((string)value);
    }
}
