using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Schemata.Abstractions.Resource;
using Schemata.Common;

namespace Schemata.Report.Skeleton;

/// <summary>Converts completed report-generation operations into report results.</summary>
public static class ReportResults
{
    /// <summary>Converts a completed operation's serialized report output to a result.</summary>
    /// <remarks>
    ///     A pending operation, terminal operation error, missing output, malformed JSON, or payload that does not
    ///     contain exactly one output branch throws <see cref="ReportException" /> with a report reason code.
    /// </remarks>
    /// <param name="operation">The operation to convert.</param>
    /// <returns>The persisted-snapshot reference or inline query response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation" /> is <see langword="null" />.</exception>
    /// <exception cref="ReportException">The operation has no valid successful report output.</exception>
    public static ReportResult FromOperation(Operation operation) {
        ArgumentNullException.ThrowIfNull(operation);

        if (!operation.Done) {
            throw new ReportException(
                ReportReasons.OperationNotComplete,
                "The report operation has not completed.");
        }

        if (operation.Error is { } error) {
            throw new ReportException(
                ReportReasons.OperationFailed,
                error.Message ?? "The report operation failed.",
                new Dictionary<string, string?> {
                    ["code"] = error.Code.ToString(CultureInfo.InvariantCulture),
                });
        }

        var output = DeserializeOutput(operation.Response?.Output);
        var hasSnapshot = !string.IsNullOrWhiteSpace(output.Snapshot);
        var hasResponse = output.Response is not null;
        if (hasSnapshot == hasResponse) {
            throw InvalidOutput();
        }

        return new() {
            Snapshot = output.Snapshot,
            Response = output.Response ?? new(),
        };
    }

    private static ReportOperationOutput DeserializeOutput(string? json) {
        if (string.IsNullOrWhiteSpace(json)) {
            throw InvalidOutput();
        }

        try {
            return JsonSerializer.Deserialize<ReportOperationOutput>(json, SchemataJson.Default) ?? throw InvalidOutput();
        } catch (JsonException) {
            throw InvalidOutput();
        }
    }

    private static ReportException InvalidOutput() {
        return new(
            ReportReasons.InvalidOperationOutput,
            "The completed report operation contains an invalid output payload.");
    }
}
