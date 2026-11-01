using System;
using Schemata.Common;

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

    /// <summary>
    ///     Determines whether an exception chain contains a known database unique-constraint violation.
    /// </summary>
    /// <param name="exception">The exception raised by the database provider.</param>
    /// <returns><see langword="true" /> when a known provider error identifies a unique-constraint violation.</returns>
    public static bool IsUniqueConstraintViolation(Exception exception) {
        for (var current = exception; current is not null; current = current.InnerException) {
            switch (current.GetType().FullName) {
                case SqliteException:
                    if (ReadIntProperty(current, "SqliteExtendedErrorCode") is 1555 or 2067) {
                        return true;
                    }

                    break;
                case MicrosoftSqlException:
                case SystemSqlException:
                    if (ReadIntProperty(current, "Number") is 2601 or 2627) {
                        return true;
                    }

                    break;
                case PostgresException:
                    if (ReadStringProperty(current, "SqlState") == "23505") {
                        return true;
                    }

                    break;
                case MySqlDataException:
                case MySqlConnectorException:
                    if (ReadIntProperty(current, "Number") == 1062) {
                        return true;
                    }

                    break;
            }
        }

        return false;
    }

    private static int? ReadIntProperty(Exception exception, string name) {
        return AppDomainTypeCache.GetProperty(exception.GetType(), name)?.GetValue(exception) is int value ? value : null;
    }

    private static string? ReadStringProperty(Exception exception, string name) {
        return AppDomainTypeCache.GetProperty(exception.GetType(), name)?.GetValue(exception) as string;
    }
}
