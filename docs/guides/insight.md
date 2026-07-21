# Insight

Add federated read queries to the Student CRUD app. This guide registers the `students` repository as
an Insight source, queries it over HTTP, adds a computed column, and projects a nested child list.

Insight reads named sources rather than exposing connection strings to callers. The caller binds a
source name to an alias, then uses that alias in filters, joins, selections, and computed expressions.

## Add the packages

Add Insight Foundation, HTTP transport, and the expression packages used by the examples:

```shell
dotnet add package --prerelease Schemata.Insight.Foundation
dotnet add package --prerelease Schemata.Insight.Http
dotnet add package --prerelease Schemata.Expressions.Aip
dotnet add package --prerelease Schemata.Expressions.Cel
dotnet add package --prerelease Schemata.Expressions.Order
```

The repository driver reads through the repository provider configured in
[Getting Started](getting-started.md).

## Enable Insight

Register the `students` resource collection as a repository-backed source:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Resource;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Expressions.Aip;
using Schemata.Expressions.Cel;
using Schemata.Expressions.Order;
using Schemata.Insight.Foundation;

builder.UseSchemata(schema => {
    var insight = schema.UseInsight(i => {
        i.WithTotalSize(TotalSizeMode.Exact);
        i.AddRepositorySource("students", "students")
         .AddSourceDriver<RepositoryDriver>(RepositoryDriver.DriverName);
    });

    insight.UseAip().UseCel().UseOrdering();
    insight.MapHttp();

    schema.Services.AddDbContextFactory<AppDbContext>(opts => opts.UseSqlite(connectionString));
    schema.Services.AddRepository<Student, EfCoreRepository<AppDbContext, Student>>();
});
```

`AddRepositorySource("students", "students")` stores a source named `students` whose repository
resource collection is also `students`. `MapHttp()` exposes `POST /v1/insight:query`.

## Run a filter and order query

Start the app:

```shell
dotnet run
```

Send a query that filters adult students, orders by age, and projects names:

```http
POST http://localhost:5000/v1/insight:query
Content-Type: application/json

{
  "sources": [{ "alias": "s", "name": "students" }],
  "transformations": [
    { "filter": { "predicate": { "source": "age >= 18" } } },
    { "order_by": { "order_by": "age desc" } }
  ],
  "selections": [
    { "field": "s.full_name", "alias": "full_name" },
    { "field": "s.age", "alias": "age" }
  ],
  "page_size": 10
}
```

The response contains rows, schema, and paging metadata:

```json
{
  "rows": [
    { "full_name": "Ada", "age": 36 },
    { "full_name": "Cleo", "age": 24 }
  ],
  "schema": [
    {
      "name": "full_name",
      "type": "string",
      "source_alias": "s",
      "is_list": false,
      "children": []
    },
    {
      "name": "age",
      "type": "int64",
      "source_alias": "s",
      "is_list": false,
      "children": []
    }
  ],
  "next_page_token": null,
  "total_size": 2,
  "unreachable": []
}
```

## Add a computed column

A computed field uses a value-capable expression language. CEL is value-capable, so set the expression
language on the slot:

```json
{
  "sources": [{ "alias": "s", "name": "students" }],
  "transformations": [
    {
      "compute": {
        "fields": [
          {
            "alias": "age_next_year",
            "expression": { "source": "double(s.age) + 1", "language": "cel" }
          }
        ]
      }
    }
  ],
  "selections": [
    { "field": "s.full_name", "alias": "full_name" },
    { "field": "age_next_year", "alias": "age_next_year" }
  ]
}
```

The compute stage runs after source rows are loaded. The response row includes the computed
`age_next_year` key.

## Add a nested selection

Add a child collection to the Student model:

```csharp
using System;
using System.Collections.Generic;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

[CanonicalName("students/{student}")]
[PrimaryKey(nameof(Uid))]
public class Student : IIdentifier, ICanonicalName
{
    public Guid             Uid         { get; set; }
    public string?          Name        { get; set; }
    public string?          CanonicalName { get; set; }
    public string?          FullName    { get; set; }
    public int              Age         { get; set; }
    public List<Enrollment> Enrollments { get; set; } = [];
}

[PrimaryKey(nameof(Uid))]
public class Enrollment
{
    public Guid    Uid    { get; set; }
    public string? Course { get; set; }
    public int     Score  { get; set; }
}
```

Add the DbSet if your context does not discover it through the navigation:

```csharp
public DbSet<Enrollment> Enrollments { get; set; } = null!;
```

Query the nested list with its own filter, order, and projection:

```json
{
  "sources": [{ "alias": "s", "name": "students" }],
  "selections": [
    { "field": "s.full_name", "alias": "full_name" },
    {
      "field": "s.enrollments",
      "alias": "passing_courses",
      "transformations": [
        { "filter": { "predicate": { "source": "e.score >= 60" } } },
        { "order_by": { "order_by": "e.score desc" } }
      ],
      "selections": [
        { "field": "e.course", "alias": "course" },
        { "field": "e.score", "alias": "score" }
      ]
    }
  ]
}
```

`RepositoryDriver` eager-loads the `Enrollments` navigation when EF Core is available. The child
pipeline then filters and orders the materialized child rows.

## Verify

A successful nested response has each parent row with a list under `passing_courses`:

```json
{
  "full_name": "Ada",
  "passing_courses": [
    { "course": "Math", "score": 95 },
    { "course": "History", "score": 82 }
  ]
}
```

## Next steps

- [Federated Query](../cookbook/federated-query.md) — join two sources and aggregate the result
- [gRPC Transport](grpc-transport.md) — add gRPC beside HTTP

## See also

- [Insight Overview](../documents/insight/overview.md) — packages, startup, and the service model
- [Insight Planning](../documents/insight/planning.md) — transformations, selections, and validation
- [Insight Drivers](../documents/insight/drivers.md) — repository driver and custom drivers
