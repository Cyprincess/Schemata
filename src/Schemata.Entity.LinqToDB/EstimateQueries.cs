using System;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;

namespace Schemata.Entity.LinqToDB;

internal static class EstimateQueries
{
    internal static EstimateProvider GetProvider(string name) {
        if (name.StartsWith("PostgreSQL", StringComparison.Ordinal)) return EstimateProvider.PostgreSql;
        if (name.StartsWith("MySql", StringComparison.Ordinal) || name.StartsWith("MariaDB", StringComparison.Ordinal)) return EstimateProvider.MySql;
        if (name.StartsWith("SqlServer", StringComparison.Ordinal)) return EstimateProvider.SqlServer;
        if (name.StartsWith("SQLite", StringComparison.Ordinal)) return EstimateProvider.Sqlite;
        return EstimateProvider.None;
    }

    internal static bool HasWhere(Expression expression) {
        var visitor = new WhereVisitor();
        visitor.Visit(expression);
        return visitor.HasWhere;
    }

    internal static bool TryParsePostgreSql(string json, out long rows) {
        rows = 0;
        try {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0) {
                root = root[0];
            }

            return root.TryGetProperty("Plan", out var plan)
                && plan.TryGetProperty("Plan Rows", out var value)
                && value.TryGetInt64(out rows);
        } catch (JsonException) {
            return false;
        }
    }

    internal static bool TryParseMySql(string json, out long rows) {
        rows = 0;
        try {
            using var document = JsonDocument.Parse(json);
            return TryFindRows(document.RootElement, out rows);
        } catch (JsonException) {
            return false;
        }
    }

    private static bool TryFindRows(JsonElement element, out long rows) {
        rows = 0;
        if (element.ValueKind == JsonValueKind.Object) {
            foreach (var property in element.EnumerateObject()) {
                if ((property.NameEquals("rows_examined_per_scan") || property.NameEquals("rows_produced_per_join"))
                 && property.Value.TryGetInt64(out rows)) {
                    return true;
                }

                if (TryFindRows(property.Value, out rows)) return true;
            }
        } else if (element.ValueKind == JsonValueKind.Array) {
            foreach (var item in element.EnumerateArray()) {
                if (TryFindRows(item, out rows)) return true;
            }
        }

        return false;
    }

    private sealed class WhereVisitor : ExpressionVisitor
    {
        internal bool HasWhere { get; private set; }

        protected override Expression VisitMethodCall(MethodCallExpression node) {
            HasWhere |= node.Method.Name == nameof(Queryable.Where)
                     && node.Method.DeclaringType == typeof(Queryable);
            return base.VisitMethodCall(node);
        }
    }
}

internal enum EstimateProvider
{
    None,
    PostgreSql,
    MySql,
    SqlServer,
    Sqlite,
}
