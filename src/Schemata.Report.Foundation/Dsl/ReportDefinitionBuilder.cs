using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Schemata.Insight.Skeleton;
using Schemata.Report.Skeleton;

namespace Schemata.Report.Foundation;

/// <summary>Builds a program-backed report query and its periodic retention metadata.</summary>
public sealed class ReportDefinitionBuilder
{
    private readonly List<SourceBinding>       _sources         = [];
    private readonly List<TransformationSpec>  _transformations = [];
    private readonly List<SelectionSpec>       _selections      = [];
    private bool                                _hasSchedule;
    private ReportRetention?                    _retention;

    internal bool               IsPeriodic { get; private set; }
    internal ReportScheduleKind ScheduleKind { get; private set; }
    internal string?            CronExpression { get; private set; }
    internal long?              IntervalTicks { get; private set; }

    /// <summary>Adds a registered Insight source binding to the query.</summary>
    /// <param name="source">Registered Insight source name.</param>
    /// <param name="alias">Request-unique source alias.</param>
    /// <returns>This definition builder.</returns>
    public ReportDefinitionBuilder From(string source, string alias) {
        _sources.Add(new(RequireText(alias, nameof(alias)), RequireText(source, nameof(source))));
        return this;
    }

    /// <summary>Filters rows with an Insight expression.</summary>
    /// <param name="expression">Filter predicate source text.</param>
    /// <param name="language">Optional expression language override.</param>
    /// <returns>This definition builder.</returns>
    public ReportDefinitionBuilder Where(string expression, string? language = null) {
        _transformations.Add(new() {
            Filter = new(new(RequireText(expression, nameof(expression)), language)),
        });
        return this;
    }

    /// <summary>Groups rows by keys and configures the group's aggregations.</summary>
    /// <param name="keys">Field paths used as group keys.</param>
    /// <param name="configure">Configures aggregations applied to each group.</param>
    /// <returns>This definition builder.</returns>
    public ReportDefinitionBuilder GroupBy(IEnumerable<string> keys, Action<ReportAggregationBuilder> configure) {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(configure);

        var groupKeys = ImmutableArray.CreateBuilder<string>();
        foreach (var key in keys) {
            groupKeys.Add(RequireText(key, "keys"));
        }

        if (groupKeys.Count == 0) {
            throw new ArgumentException("At least one group key is required.", nameof(keys));
        }

        var aggregations = new ReportAggregationBuilder();
        configure(aggregations);
        _transformations.Add(new() {
            GroupBy = new(groupKeys.ToImmutable(), aggregations.ToImmutable()),
        });
        return this;
    }

    /// <summary>Selects a field path for the report result.</summary>
    /// <param name="field">Field path to include in the result.</param>
    /// <returns>This definition builder.</returns>
    public ReportDefinitionBuilder Select(string field) {
        _selections.Add(new() { Field = RequireText(field, nameof(field)) });
        return this;
    }

    /// <summary>Selects a computed expression under an output alias.</summary>
    /// <param name="expression">Computed expression source text.</param>
    /// <param name="alias">Output alias for the expression.</param>
    /// <param name="language">Optional expression language override.</param>
    /// <returns>This definition builder.</returns>
    public ReportDefinitionBuilder SelectExpression(string expression, string alias, string? language = null) {
        _selections.Add(new() {
            Expression = new(RequireText(expression, nameof(expression)), language),
            Alias      = RequireText(alias, nameof(alias)),
        });
        return this;
    }

    /// <summary>Marks the report as periodic with a cron expression or a positive interval.</summary>
    /// <param name="cron">Cron schedule expression.</param>
    /// <param name="interval">Fixed recurrence interval.</param>
    /// <returns>This definition builder.</returns>
    public ReportDefinitionBuilder Periodic(string? cron = null, TimeSpan? interval = null) {
        if (_hasSchedule) {
            throw new InvalidOperationException("A report definition can have only one schedule.");
        }

        if ((cron is null && interval is null) || (cron is not null && interval is not null)) {
            throw new ArgumentException("Specify exactly one of cron or interval.");
        }

        if (cron is not null) {
            CronExpression = RequireText(cron, nameof(cron));
            ScheduleKind   = ReportScheduleKind.Cron;
        } else {
            var recurrence = interval ?? throw new ArgumentException("Specify exactly one of cron or interval.");
            if (recurrence <= TimeSpan.Zero) {
                throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be positive.");
            }

            IntervalTicks = recurrence.Ticks;
            ScheduleKind  = ReportScheduleKind.Periodic;
        }

        IsPeriodic  = true;
        _hasSchedule = true;
        return this;
    }

    /// <summary>Sets the maximum age and/or count of persisted report snapshots.</summary>
    /// <param name="days">Maximum snapshot age in whole days.</param>
    /// <param name="count">Maximum number of snapshots to retain.</param>
    /// <returns>This definition builder.</returns>
    public ReportDefinitionBuilder Retain(int? days = null, int? count = null) {
        if (days is null && count is null) {
            throw new ArgumentException("Specify days, count, or both.");
        }

        if (days <= 0) {
            throw new ArgumentOutOfRangeException(nameof(days), "Retention days must be positive.");
        }

        if (count <= 0) {
            throw new ArgumentOutOfRangeException(nameof(count), "Retention count must be positive.");
        }

        _retention = new() { MaxAgeDays = days, MaxCount = count };
        return this;
    }

    internal QueryInsightRequest Build() {
        var request = new QueryInsightRequest();
        foreach (var source in _sources) {
            request.Sources.Add(new(source.Alias, source.Name));
        }

        foreach (var transformation in _transformations) {
            request.Transformations.Add(Clone(transformation));
        }

        foreach (var selection in _selections) {
            request.Selections.Add(Clone(selection));
        }

        return request;
    }

    internal ReportDefinitionRegistration ToRegistration(string name) {
        return new() {
            Name           = name,
            SourceKind     = ReportSourceKind.Program,
            Periodic       = IsPeriodic,
            ScheduleKind   = ScheduleKind,
            CronExpression = CronExpression,
            IntervalTicks  = IntervalTicks,
            Retention = _retention is null
                ? null
                : new() { MaxAgeDays = _retention.MaxAgeDays, MaxCount = _retention.MaxCount },
            Provider = name,
        };
    }

    private static TransformationSpec Clone(TransformationSpec source) {
        if (source.Filter is { } filter) {
            return new() { Filter = new(new(filter.Predicate.Source, filter.Predicate.Language)) };
        }

        var group = source.GroupBy!;
        return new() {
            GroupBy = new(group.Keys, ImmutableArray.CreateRange(group.Aggregations)),
        };
    }

    private static SelectionSpec Clone(SelectionSpec source) {
        return new() {
            Field      = source.Field,
            Alias      = source.Alias,
            Expression = source.Expression is { } expression
                ? new(expression.Source, expression.Language)
                : null,
        };
    }

    private static string RequireText(string? value, string parameter) {
        if (string.IsNullOrWhiteSpace(value)) {
            throw new ArgumentException($"{parameter} must not be empty or whitespace.", parameter);
        }

        return value;
    }
}
