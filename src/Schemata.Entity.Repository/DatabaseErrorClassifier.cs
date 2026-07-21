using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace Schemata.Entity.Repository;

/// <summary>
///     Classifies database provider exceptions without referencing provider-specific packages.
/// </summary>
public static class DatabaseErrorClassifier
{
    private const string SqliteException = "Microsoft.Data.Sqlite.SqliteException";
    private const string MicrosoftSqlException = "Microsoft.Data.SqlClient.SqlException";
    private const string SystemSqlException = "System.Data.SqlClient.SqlException";
    private const string PostgresException = "Npgsql.PostgresException";
    private const string MySqlDataException = "MySql.Data.MySqlClient.MySqlException";
    private const string MySqlConnectorException = "MySqlConnector.MySqlException";

    private static readonly ConcurrentDictionary<Type, PropertyInfo> ExtendedErrorCodeProperties = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo> NumberProperties            = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo> SqlStateProperties          = new();

    /// <summary>
    ///     Determines whether an exception chain contains a known database unique-constraint violation.
    /// </summary>
    /// <param name="exception">The exception raised by the database provider.</param>
    /// <returns><see langword="true" /> when a known provider error identifies a unique-constraint violation.</returns>
    public static bool IsUniqueConstraintViolation(Exception exception) {
        for (var current = exception; current is not null; current = current.InnerException) {
            switch (current.GetType().FullName) {
                case SqliteException:
                    if (ReadIntProperty(current, ExtendedErrorCodeProperties, "SqliteExtendedErrorCode") is 1555 or 2067) {
                        return true;
                    }

                    break;
                case MicrosoftSqlException:
                case SystemSqlException:
                    if (ReadIntProperty(current, NumberProperties, "Number") is 2601 or 2627) {
                        return true;
                    }

                    break;
                case PostgresException:
                    if (ReadStringProperty(current, SqlStateProperties, "SqlState") == "23505") {
                        return true;
                    }

                    break;
                case MySqlDataException:
                case MySqlConnectorException:
                    if (ReadIntProperty(current, NumberProperties, "Number") == 1062) {
                        return true;
                    }

                    break;
            }
        }

        return false;
    }

    private static int? ReadIntProperty(
        Exception                                    exception,
        ConcurrentDictionary<Type, PropertyInfo> properties,
        string                                       name
    ) {
        return GetProperty(exception.GetType(), properties, name)?.GetValue(exception) is int value ? value : null;
    }

    private static string? ReadStringProperty(
        Exception                                    exception,
        ConcurrentDictionary<Type, PropertyInfo> properties,
        string                                       name
    ) {
        return GetProperty(exception.GetType(), properties, name)?.GetValue(exception) as string;
    }

    private static PropertyInfo? GetProperty(
        Type                                         type,
        ConcurrentDictionary<Type, PropertyInfo> properties,
        string                                       name
    ) {
        if (properties.TryGetValue(type, out var property)) {
            return property;
        }

        property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        return property is null ? null : properties.GetOrAdd(type, property);
    }
}
