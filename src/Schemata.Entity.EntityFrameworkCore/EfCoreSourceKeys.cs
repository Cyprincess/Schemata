using System;
using System.Data.Common;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Schemata.Entity.Repository;

namespace Schemata.Entity.EntityFrameworkCore;

/// <summary>
///     Shared extraction of the physical data-source identity behind an EF Core context, used by
///     both the repository and the unit of work to build their <see cref="IRepositorySource.SourceKey" />.
/// </summary>
internal static class EfCoreSourceKeys
{
    public static string CreateKey(DbContext context, object factory, RepositoryBinding binding) {
        var database = context.Database;
        string source;
        if (database.IsRelational()) {
            var connection = database.GetDbConnection();
            source = IsEphemeralDataSource(connection)
                ? FactoryIdentity(factory)
                : DescribeSource(connection);
        } else {
            // Non-relational providers keep one unnamed store per service provider, so the
            // owning factory instance is the only usable resource identity.
            source = FactoryIdentity(factory);
        }

        return QueryCacheKey.CreateSourceKey(database.ProviderName ?? context.GetType().Name, source, binding.Namespace);
    }

    // Object identity is only a resource identity within the owning process; it never keys a
    // cross-process cache.
    private static string FactoryIdentity(object factory) {
        return $"factory:{RuntimeHelpers.GetHashCode(factory).ToString("x", CultureInfo.InvariantCulture)}";
    }

    // Full connection identity joins the hash: a redacted credential after the first open only
    // costs a cache miss, while a principal subset would collide across security contexts.
    internal static string DescribeSource(DbConnection connection) {
        return Frame(connection.GetType().ToString())
             + Frame(connection.DataSource)
             + Frame(connection.Database)
             + Frame(connection.ConnectionString);
    }

    internal static bool IsEphemeralDataSource(DbConnection connection) {
        var dataSource = connection.DataSource;

        return string.IsNullOrEmpty(dataSource)
            || dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase)
            || HasMemoryLifetime(connection.ConnectionString);
    }

    private static string Frame(string? value) {
        var content = value ?? string.Empty;

        return $"{Encoding.UTF8.GetByteCount(content)}:{content}";
    }

    private static bool HasMemoryLifetime(string? connectionString) {
        if (string.IsNullOrEmpty(connectionString)) {
            return false;
        }

        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };

        return builder.TryGetValue("Mode", out var mode)
            && mode is string lifetime
            && lifetime.Equals("Memory", StringComparison.OrdinalIgnoreCase);
    }
}
