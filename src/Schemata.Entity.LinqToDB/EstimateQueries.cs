using System;
using System.Linq;
using System.Linq.Expressions;
using LinqToDB;

namespace Schemata.Entity.LinqToDB;

internal static class EstimateQueries
{
    internal static EstimateProvider GetProvider(string name) {
        if (name.StartsWith("PostgreSQL", StringComparison.Ordinal)) return EstimateProvider.PostgreSql;
        if (name.StartsWith("MySql", StringComparison.Ordinal)) return EstimateProvider.MySql;
        if (name.StartsWith("SqlServer", StringComparison.Ordinal)) return EstimateProvider.SqlServer;
        if (name.StartsWith("SQLite", StringComparison.Ordinal)) return EstimateProvider.Sqlite;
        return EstimateProvider.None;
    }

    internal static bool IsTableRoot<TEntity>(Expression expression, string tableName) {
        if (expression is not MethodCallExpression call || !call.Method.IsGenericMethod
         || call.Method.GetGenericArguments()[0] != typeof(TEntity)) return false;
        if (call.Method.DeclaringType == typeof(Queryable) && call.Method.Name == nameof(Queryable.OfType)) {
            return IsTableRoot<TEntity>(call.Arguments[0], tableName);
        }
        if (call.Method.DeclaringType == typeof(LinqExtensions) && call.Method.Name == nameof(LinqExtensions.TableName)) {
            return call.Arguments[1] is ConstantExpression { Value: string name } && name == tableName
                && IsTableRoot<TEntity>(call.Arguments[0], tableName);
        }
        return call.Method.DeclaringType == typeof(DataExtensions) && call.Method.Name == nameof(DataExtensions.GetTable)
            && call.Arguments.Count == 1;
    }

    internal static bool HasMySqlUnsupportedShape(Expression expression) {
        var visitor = new MySqlShapeVisitor();
        visitor.Visit(expression);
        return visitor.Unsupported;
    }

    private sealed class MySqlShapeVisitor : ExpressionVisitor
    {
        internal bool Unsupported { get; private set; }

        protected override Expression VisitMethodCall(MethodCallExpression node) {
            if ((node.Method.DeclaringType == typeof(Queryable) || node.Method.DeclaringType == typeof(LinqExtensions))
             && node.Method.Name is nameof(Queryable.Take) or nameof(Queryable.Skip) or nameof(Queryable.GroupBy)
                 or nameof(Queryable.Distinct) or nameof(Queryable.Union) or nameof(Queryable.Intersect)
                 or nameof(Queryable.Except) or nameof(Queryable.Concat)) Unsupported = true;
            return base.VisitMethodCall(node);
        }
    }
}
