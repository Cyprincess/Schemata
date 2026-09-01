using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Schemata.Report.Tests.Fixtures;

internal static class ReportTestRows
{
    internal static IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Create(int count) {
        return ToAsync(Enumerable.Range(0, count).Select(value => Row(value)));
    }

    internal static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> ThrowAfter(int count, string message) {
        foreach (var value in Enumerable.Range(0, count)) {
            yield return Row(value);
            await Task.CompletedTask;
        }

        throw new InvalidOperationException(message);
    }

    internal static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> rows) {
        foreach (var row in rows) {
            yield return row;
            await Task.CompletedTask;
        }
    }

    private static IReadOnlyDictionary<string, object?> Row(int value) {
        return new Dictionary<string, object?> { ["value"] = value };
    }
}