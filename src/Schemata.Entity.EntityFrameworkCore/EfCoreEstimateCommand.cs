using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Schemata.Entity.EntityFrameworkCore;

internal static class EfCoreEstimateCommand
{
    internal static async Task<string> ReadAsync(DbContext context, DbCommand source, string sql, CancellationToken ct) {
        var builder = context.GetService<IRelationalCommandBuilderFactory>().Create();
        builder.Append(sql);
        while (source.Parameters.Count != 0) {
            var parameter = source.Parameters[0];
            source.Parameters.RemoveAt(0);
            builder.AddRawParameter(parameter.ParameterName, parameter);
        }
        await using var reader = await builder.Build().ExecuteReaderAsync(Parameters(context, CommandSource.LinqQuery), ct);
        if (!await reader.DbDataReader.ReadAsync(ct) || reader.DbDataReader.GetValue(0) is not string plan) {
            throw new FormatException("The query plan result is not text.");
        }
        return plan;
    }

    internal static async Task ExecuteModeAsync(DbContext context, string sql, int timeout, CancellationToken ct) {
        var connection = context.GetService<IRelationalConnection>();
        var previous = connection.CommandTimeout;
        connection.CommandTimeout = timeout;
        try {
            var builder = context.GetService<IRelationalCommandBuilderFactory>().Create();
            builder.Append(sql);
            await builder.Build().ExecuteNonQueryAsync(Parameters(context, CommandSource.ExecuteSqlRaw), ct);
        } finally {
            connection.CommandTimeout = previous;
        }
    }

    private static RelationalCommandParameterObject Parameters(DbContext context, CommandSource source) => new(
        context.GetService<IRelationalConnection>(), null, null, context,
        context.GetService<IRelationalCommandDiagnosticsLogger>(), source);
}
