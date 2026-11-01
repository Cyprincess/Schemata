using System;
using System.Data.Common;
using System.Globalization;
using System.Runtime.CompilerServices;
using LinqToDB.Data;
using Schemata.Entity.Repository;

namespace Schemata.Entity.LinqToDB;

/// <summary>
///     Shared extraction of the physical data-source identity behind a LINQ to DB connection,
///     used by both the repository and the unit of work to build their
///     <see cref="IRepositorySource.SourceKey" />.
/// </summary>
internal static class LinqToDbSourceKeys
{
    public static string CreateKey(DataConnection context, object factory, RepositoryBinding binding) {
        var source = context.ConnectionString;
        if (string.IsNullOrEmpty(source)
         || (context.DataProvider.Name.StartsWith("SQLite", StringComparison.OrdinalIgnoreCase) && IsEphemeralDataSource(source))) {
            source = $"factory:{RuntimeHelpers.GetHashCode(factory).ToString("x", CultureInfo.InvariantCulture)}";
        }

        return QueryCacheKey.CreateSourceKey(context.DataProvider.Name, source, binding.Namespace);
    }

    // SQLite named in-memory lifetimes (Mode=Memory, Data Source=:memory:) reuse one connection
    // string for distinct databases, so their identity must not key a cross-scope cache.
    internal static bool IsEphemeralDataSource(string? source) {
        if (string.IsNullOrEmpty(source)) {
            return true;
        }

        var builder = new DbConnectionStringBuilder { ConnectionString = source };

        if (builder.TryGetValue("Mode", out var mode)
         && mode is string lifetime
         && lifetime.Equals("Memory", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        if (!builder.TryGetValue("Data Source", out var database)
         && !builder.TryGetValue("DataSource", out database)
         && !builder.TryGetValue("Filename", out database)) {
            return true;
        }

        return database is not string path || path.Length == 0 || path.Equals(":memory:", StringComparison.OrdinalIgnoreCase);
    }
}
