using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reflection;
using LinqToDB;
using LinqToDB.Data;
using Schemata.Abstractions.Entities;

namespace Schemata.Entity.LinqToDB.Integration.Tests.Fixtures;

internal static class SchemaExtensions
{
    internal static void CreateTableWithIndexes<TEntity>(this DataConnection connection)
        where TEntity : class {
        connection.CreateTable<TEntity>(tableOptions: TableOptions.CreateIfNotExists);

        var table = typeof(TEntity).GetCustomAttribute<TableAttribute>()?.Name ?? typeof(TEntity).Name;
        foreach (var index in typeof(TEntity).GetCustomAttributes<IndexAttribute>()) {
            connection.Execute(CreateIndexSql(connection.DataProvider.Name, table, index));
        }
    }

    internal static string CreateIndexSql(string providerName, string table, IndexAttribute index) {
        var quote   = GetQuote(providerName);
        var name    = $"IX_{table}_{string.Join("_", index.Properties)}";
        var columns = string.Join(", ", index.Properties.Select(quote));
        var create  = $"CREATE {(index.IsUnique ? "UNIQUE " : string.Empty)}INDEX {quote(name)} ON {quote(table)} ({columns})";

        return providerName.StartsWith("SqlServer", StringComparison.Ordinal)
            ? $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'{name.Replace("'", "''")}') {create}"
            : create.Replace("INDEX ", "INDEX IF NOT EXISTS ", StringComparison.Ordinal);
    }

    private static Func<string, string> GetQuote(string providerName) {
        if (providerName.StartsWith("SqlServer", StringComparison.Ordinal)) return value => $"[{value.Replace("]", "]]", StringComparison.Ordinal)}]";
        if (providerName.StartsWith("MySql", StringComparison.Ordinal) || providerName.StartsWith("MariaDB", StringComparison.Ordinal)) return value => $"`{value.Replace("`", "``", StringComparison.Ordinal)}`";
        return value => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
