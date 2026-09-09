using System;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Schemata.Entity.Repository.Estimation;

public static class QueryPlanEstimate
{
    /// <summary>
    ///     Reads a non-executing plan for a parameterized SELECT. The caller owns the open connection,
    ///     command, and transaction, and must serialize access to the connection during estimation.
    ///     SQL Server restoration failure closes the connection and propagates the failure.
    /// </summary>
    public static async Task<long?> EstimateAsync(DbCommand command, QueryEstimateProvider provider, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        var sql = command.CommandText;
        try {
            switch (provider) {
                case QueryEstimateProvider.PostgreSql:
                    command.CommandText = "EXPLAIN (FORMAT JSON) " + sql;
                    break;
                case QueryEstimateProvider.MySql:
                    command.CommandText = "EXPLAIN FORMAT=JSON " + sql;
                    break;
                case QueryEstimateProvider.SqlServer:
                    return await EstimateSqlServerAsync(command.Connection!, command.Transaction, command.CommandTimeout,
                        async token => await command.ExecuteScalarAsync(token) as string
                            ?? throw new FormatException("The query plan result is not text."), ct);
                default:
                    throw new ArgumentOutOfRangeException(nameof(provider), provider, null);
            }

            var plan = await command.ExecuteScalarAsync(ct) as string
                ?? throw new FormatException("The query plan result is not text.");
            return Parse(plan, provider);
        } finally {
            command.CommandText = sql;
        }
    }

    /// <summary>
    ///     Owns SHOWPLAN mode on an open, exclusively used connection. The callback must execute only
    ///     the parameterized SELECT on this connection and transaction, leaving both open.
    ///     A failed restoration closes the connection; cancellation never skips restoration.
    /// </summary>
    public static async Task<long?> EstimateSqlServerAsync(
        DbConnection connection, DbTransaction? transaction, int commandTimeout,
        Func<CancellationToken, Task<string>> readPlan, CancellationToken ct = default
    ) {
        ct.ThrowIfCancellationRequested();
        await using var mode = connection.CreateCommand();
        mode.Transaction = transaction;
        mode.CommandTimeout = commandTimeout >= 0 ? commandTimeout : mode.CommandTimeout;
        mode.CommandText = "SET SHOWPLAN_XML ON";
        Exception? failure = null;
        try {
            await mode.ExecuteNonQueryAsync(ct);
            return Parse(await readPlan(ct), QueryEstimateProvider.SqlServer);
        } catch (Exception error) {
            failure = error;
            throw;
        } finally {
            try {
                mode.CommandText = "SET SHOWPLAN_XML OFF";
                mode.CommandTimeout = commandTimeout > 0 ? Math.Min(commandTimeout, 30) : 30;
                await mode.ExecuteNonQueryAsync(CancellationToken.None);
            } catch (Exception restoration) {
                Exception cleanup = restoration;
                try {
                    await connection.CloseAsync();
                } catch (Exception close) {
                    cleanup = new AggregateException(restoration, close);
                }

                if (failure is not null) throw new AggregateException(failure, cleanup);
                throw new AggregateException("Failed to restore SQL Server SHOWPLAN mode.", cleanup);
            }
        }
    }

    /// <summary>
    ///     Returns the top-level result estimate, or null for a well-formed unsupported plan shape.
    ///     Invalid plan data raises a parsing exception.
    /// </summary>
    public static long? Parse(string plan, QueryEstimateProvider provider) {
        switch (provider) {
            case QueryEstimateProvider.PostgreSql: {
                using var document = JsonDocument.Parse(plan);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0) {
                    throw new FormatException("Expected a PostgreSQL plan array.");
                }
                if (root.GetArrayLength() != 1) return null;
                return ReadRows(RequireObject(root[0].GetProperty("Plan")).GetProperty("Plan Rows"));
            }
            case QueryEstimateProvider.MySql: {
                using var document = JsonDocument.Parse(plan);
                return ParseMySql(RequireObject(document.RootElement.GetProperty("query_block")));
            }
            case QueryEstimateProvider.SqlServer: {
                var document = XDocument.Parse(plan);
                XNamespace ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";
                if (document.Root?.Name != ns + "ShowPlanXML") throw new FormatException("Expected SQL Server ShowPlanXML.");
                var statements = document.Root.Element(ns + "BatchSequence")?.Elements(ns + "Batch")
                    .SelectMany(batch => batch.Elements(ns + "Statements").SelectMany(items => items.Elements())).ToArray()
                    ?? throw new FormatException("Missing SQL Server plan statements.");
                if (statements.Length == 0) throw new FormatException("Missing SQL Server plan statement.");
                if (statements.Length != 1 || statements[0].Name != ns + "StmtSimple") return null;
                var root = statements[0].Element(ns + "QueryPlan")?.Element(ns + "RelOp");
                if (root is null) throw new FormatException("Missing SQL Server result operator.");
                var rows = root.Attribute("EstimateRows")?.Value
                    ?? throw new FormatException("Missing SQL Server result cardinality.");
                return ConvertRows(decimal.Parse(rows, NumberStyles.Float, CultureInfo.InvariantCulture));
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(provider), provider, null);
        }
    }

    private static long? ParseMySql(JsonElement node) {
        RequireObject(node);
        if (node.TryGetProperty("grouping_operation", out _) || node.TryGetProperty("duplicates_removal", out _)
         || node.TryGetProperty("union_result", out _) || node.TryGetProperty("windowing", out _)) return null;
        if (node.TryGetProperty("ordering_operation", out var ordering)) return ParseMySql(ordering);
        if (node.TryGetProperty("nested_loop", out var loop)) {
            if (loop.ValueKind != JsonValueKind.Array || loop.GetArrayLength() == 0) {
                throw new FormatException("Expected a nonempty MySQL nested loop.");
            }
            // Semijoins and duplicate elimination change the output cardinality beyond a scanned table.
            foreach (var item in loop.EnumerateArray()) {
                var child = RequireObject(item);
                if (!child.TryGetProperty("table", out var table)) return null;
                RequireObject(table);
                if (table.TryGetProperty("first_match", out _) || table.TryGetProperty("loosescan", out _)
                 || table.TryGetProperty("not_exists", out _)) return null;
            }
            return ReadMySqlTable(loop[loop.GetArrayLength() - 1].GetProperty("table"));
        }
        if (node.TryGetProperty("table", out var single)) return ReadMySqlTable(single);
        if (node.TryGetProperty("message", out _)) return null;
        throw new FormatException("Unrecognized MySQL result plan.");
    }

    private static long? ReadMySqlTable(JsonElement table) {
        RequireObject(table);
        if (table.TryGetProperty("first_match", out _) || table.TryGetProperty("loosescan", out _)
         || table.TryGetProperty("not_exists", out _)) return null;
        return table.TryGetProperty("rows_produced_per_join", out var rows) ? ReadRows(rows) : null;
    }

    private static JsonElement RequireObject(JsonElement value) {
        if (value.ValueKind != JsonValueKind.Object) throw new FormatException("Expected a query plan object.");
        return value;
    }

    private static long ReadRows(JsonElement value) {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var rows)) {
            throw new FormatException("Invalid query plan cardinality.");
        }
        return ConvertRows(rows);
    }

    private static long ConvertRows(decimal rows) {
        if (rows < 0 || rows > long.MaxValue) throw new FormatException("Query plan cardinality is outside Int64 bounds.");
        return checked((long)decimal.Ceiling(rows));
    }
}
