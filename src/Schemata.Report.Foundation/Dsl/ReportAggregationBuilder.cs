using System.Collections.Generic;
using System.Collections.Immutable;
using Schemata.Insight.Skeleton;

namespace Schemata.Report.Foundation;

/// <summary>Configures aggregations emitted by a report group-by transformation.</summary>
public sealed class ReportAggregationBuilder
{
    private readonly List<AggregationSpec> _aggregations = [];

    /// <summary>Adds a sum aggregation.</summary>
    /// <param name="field">Field path to aggregate.</param>
    /// <param name="into">Output alias.</param>
    /// <returns>This aggregation builder.</returns>
    public ReportAggregationBuilder Sum(string field, string into) => Add(field, AggregationFunction.Sum, into);

    /// <summary>Adds an average aggregation.</summary>
    /// <param name="field">Field path to aggregate.</param>
    /// <param name="into">Output alias.</param>
    /// <returns>This aggregation builder.</returns>
    public ReportAggregationBuilder Avg(string field, string into) => Add(field, AggregationFunction.Avg, into);

    /// <summary>Adds a minimum aggregation.</summary>
    /// <param name="field">Field path to aggregate.</param>
    /// <param name="into">Output alias.</param>
    /// <returns>This aggregation builder.</returns>
    public ReportAggregationBuilder Min(string field, string into) => Add(field, AggregationFunction.Min, into);

    /// <summary>Adds a maximum aggregation.</summary>
    /// <param name="field">Field path to aggregate.</param>
    /// <param name="into">Output alias.</param>
    /// <returns>This aggregation builder.</returns>
    public ReportAggregationBuilder Max(string field, string into) => Add(field, AggregationFunction.Max, into);

    /// <summary>Adds a count aggregation.</summary>
    /// <param name="field">Field path to aggregate.</param>
    /// <param name="into">Output alias.</param>
    /// <returns>This aggregation builder.</returns>
    public ReportAggregationBuilder Count(string field, string into) => Add(field, AggregationFunction.Count, into);

    /// <summary>Adds a distinct-count aggregation.</summary>
    /// <param name="field">Field path to aggregate.</param>
    /// <param name="into">Output alias.</param>
    /// <returns>This aggregation builder.</returns>
    public ReportAggregationBuilder CountDistinct(string field, string into) => Add(field, AggregationFunction.CountDistinct, into);

    internal ImmutableArray<AggregationSpec> ToImmutable() {
        return ImmutableArray.CreateRange(_aggregations);
    }

    private ReportAggregationBuilder Add(string field, AggregationFunction function, string into) {
        _aggregations.Add(new(RequireText(field, nameof(field)), function, RequireText(into, nameof(into))));
        return this;
    }

    private static string RequireText(string? value, string parameter) {
        if (string.IsNullOrWhiteSpace(value)) {
            throw new System.ArgumentException($"{parameter} must not be empty or whitespace.", parameter);
        }

        return value;
    }
}
