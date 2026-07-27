# Report definitions

A report definition resolves to one `QueryInsightRequest`. Reports locate a definition through
`IReportDefinitionStore`, then pass the resolved query through `IReportDefinitionAdvisor` before
Insight builds the plan.

## Definition sources and precedence

`SchemataReportFeature` registers exactly two `IReportDefinitionSource` implementations in this
order:

| Order | Source | Resolves |
| ---: | --- | --- |
| 1 | `ConfigurationReportDefinitionStore` | Entries in `SchemataReportOptions.Definitions`, including expression definitions and DSL definitions. |
| 2 | `DatabaseReportDefinitionStore<TReport>` | Persisted `TReport` rows through `IRepository<TReport>`. |

`CompositeReportDefinitionStore` resolves sources in DI registration order and returns the first
result. A configuration definition therefore wins when configuration and database definitions share a
name. Its periodic listing also suppresses duplicate names in that same order.

Program-backed definitions are held by `ConfigurationReportDefinitionStore`. `Define` registers an
`IReportDefinitionProvider` under the report name and writes a program registration into
`SchemataReportOptions.Definitions`; the provider is not a third top-level definition store.

## Persisted entities and names

The three Report entities carry these `[CanonicalName]` patterns:

| Entity | Pattern | Role |
| --- | --- | --- |
| `SchemataReport` | `reports/{report}` | Definition row. |
| `SchemataReportSnapshot` | `reports/{report}/snapshots/{snapshot}` | Persisted materialization header. |
| `SchemataReportSnapshotChunk` | `reports/{report}/snapshots/{snapshot}/chunks/{chunk}` | Internal persisted row-data chunk. |

[AIP-122](https://google.aip.dev/122) specifies resource names as URI-path schemas without a leading
slash and directs collection identifiers to use plural nouns. `SchemataReport` and
`SchemataReportSnapshot` are registered by the Report transport features. The chunk entity remains an
internal storage type: the transport features never register it as a resource, and
`SchemataReportSnapshotChunkShould.Chunk_Is_Not_Auto_Registered_As_Resource` asserts that boundary.

`SchemataReport` stores an expression definition in `Definition`, selects its kind through
`SourceKind`, and carries `Provider`, `Periodic`, `ScheduleKind`, `CronExpression`, `IntervalTicks`,
and chunk counts, schema, and an error message when materialization fails.

## Configuration definitions

Add an expression definition through `SchemataReportOptions.Definitions`. `Query` identifies an
expression-backed registration; `SourceKind` defaults to `ReportSourceKind.Expression`.

```csharp
using Microsoft.AspNetCore.Builder;
using Schemata.Insight.Skeleton;
using Schemata.Report.Foundation;

builder.UseSchemata(schema => {
    schema.UseReport(options => options.Definitions.Add(new() {
        Name = "student-roster",
        Query = new QueryInsightRequest {
            Sources = [new("student", "students")],
            Selections = [new() { Field = "full_name" }],
        },
    }));
});
```

`ConfigurationReportDefinitionStore` materializes a `SchemataReport` from the registration. An
expression registration without `Query` raises `InvalidOperationException` when resolution occurs.

## Program definition DSL

`SchemataReportBuilder<TReport, TSnapshot, TChunk>.Define(string, Action<ReportDefinitionBuilder>)`
adds a program-backed definition. It accepts one unique non-empty name per builder and returns `void`.

```csharp
using Microsoft.AspNetCore.Builder;

builder.UseSchemata(schema => {
    var reports = schema.UseReport();
    reports.Define("student-roster", definition => definition
        .From("students", alias: "student")
        .Where("age >= 18")
        .Select("full_name")
        .Select("age"));
});
```

| `ReportDefinitionBuilder` method | Effect |
| --- | --- |
| `From(string source, string alias)` | Binds a registered Insight source to a request alias. |
| `Where(string expression, string? language = null)` | Adds an Insight filter. |
| `GroupBy(IEnumerable<string> keys, Action<ReportAggregationBuilder> configure)` | Adds grouping and aggregation definitions. |
| `Select(string field)` | Projects a field. |
| `SelectExpression(string expression, string alias, string? language = null)` | Projects a computed expression under an alias. |
| `Periodic(string? cron = null, TimeSpan? interval = null)` | Marks the definition periodic with exactly one schedule form. |
| `Retain(int? days = null, int? count = null)` | Sets the successful-snapshot age and/or count limits. |

`ProgramReportDefinitionProvider.GetDefinitionAsync` builds a fresh `QueryInsightRequest` from the
DSL definition. Its keyed DI registration uses the report name unless a registration specifies a
different `Provider` key.

## Database definitions

`DatabaseReportDefinitionStore<TReport>` reads the matching `TReport` row from
`IRepository<TReport>`. An expression-backed row deserializes `Definition` as `QueryInsightRequest`;
a program-backed row resolves its `Provider` as keyed `IReportDefinitionProvider`. A missing
repository fails when this source resolves a definition, rather than during Report startup.

## See also

- [Overview](overview.md) — startup, options, and repository boundaries
- [Generation](generation.md) — named and inline execution
- [Scheduling](scheduling.md) — periodic metadata and initialization
