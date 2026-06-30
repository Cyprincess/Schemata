# Federated Query

## What you'll build

A working Insight setup with two repository-backed sources. You will register customers and purchases,
run a joined sales summary with a compute and group-by pipeline, then run a nested customer drill-down
that filters and projects child orders.

Insight treats source names as catalog entries. The request binds each source name to a short alias and
uses those aliases in expressions and selections.

## Prerequisites

- The Student CRUD setup from [Getting Started](../guides/getting-started.md).
- EF Core repository configured as in the repository guides.
- Packages: `Schemata.Insight.Foundation`, `Schemata.Insight.Http`, `Schemata.Expressions.Aip`,
  `Schemata.Expressions.Cel`, and `Schemata.Expressions.Order`.

## Step 1: Add the entities

```csharp
using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

[CanonicalName("customers/{customer}")]
[PrimaryKey(nameof(Uid))]
public class Customer : IIdentifier, ICanonicalName
{
    public Guid        Uid           { get; set; }
    public string?     Name          { get; set; }
    public string?     CanonicalName { get; set; }
    public int         Id            { get; set; }
    public string?     FullName      { get; set; }
    public List<Order> Orders        { get; set; } = [];
}

[PrimaryKey(nameof(Uid))]
public class Order
{
    public Guid    Uid    { get; set; }
    public int     Number { get; set; }
    public string? Status { get; set; }
    public int     Amount { get; set; }
    public int     Placed { get; set; }
}

[CanonicalName("purchases/{purchase}")]
[PrimaryKey(nameof(Uid))]
public class Purchase : IIdentifier, ICanonicalName
{
    public Guid    Uid           { get; set; }
    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }
    public int     CustomerId    { get; set; }
    public int     Amount        { get; set; }
    public string? Status        { get; set; }
}
```

**Assertion:** the project builds with `Customer`, `Order`, and `Purchase` in the application assembly.

## Step 2: Add DbSet properties

```csharp
using Microsoft.EntityFrameworkCore;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Customer> Customers { get; set; } = null!;

    public DbSet<Purchase> Purchases { get; set; } = null!;
}
```

**Assertion:** EF Core can create the model with `Customers`, owned `Orders` navigation rows, and
`Purchases`.

## Step 3: Register Insight sources

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
        i.AddRepositorySource("customers", "customers")
         .AddRepositorySource("purchases", "purchases")
         .AddSourceDriver<RepositoryDriver>(RepositoryDriver.DriverName);
    });

    insight.UseAip().UseCel().UseOrdering();
    insight.MapHttp();

    schema.Services.AddDbContextFactory<AppDbContext>(opts => opts.UseSqlite(connectionString));
    schema.Services.AddRepository<Customer, EfCoreRepository<AppDbContext, Customer>>();
    schema.Services.AddRepository<Purchase, EfCoreRepository<AppDbContext, Purchase>>();
});
```

**Assertion:** the application starts and `POST /v1/insight:query` is available.

## Step 4: Seed a small data set

```csharp
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using var scope = app.Services.CreateScope();
var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
using var db = factory.CreateDbContext();

db.Customers.AddRange(
    new Customer {
        Uid = Guid.NewGuid(), Id = 1, Name = "ada", FullName = "Ada",
        Orders = [
            new Order { Uid = Guid.NewGuid(), Number = 1, Status = "paid", Amount = 100, Placed = 3 },
            new Order { Uid = Guid.NewGuid(), Number = 2, Status = "open", Amount = 50, Placed = 2 }
        ]
    },
    new Customer {
        Uid = Guid.NewGuid(), Id = 2, Name = "bob", FullName = "Bob",
        Orders = [
            new Order { Uid = Guid.NewGuid(), Number = 3, Status = "paid", Amount = 200, Placed = 5 }
        ]
    });

db.Purchases.AddRange(
    new Purchase { Uid = Guid.NewGuid(), CustomerId = 1, Amount = 100, Status = "paid" },
    new Purchase { Uid = Guid.NewGuid(), CustomerId = 1, Amount = 50, Status = "open" },
    new Purchase { Uid = Guid.NewGuid(), CustomerId = 2, Amount = 200, Status = "paid" });

db.SaveChanges();
```

**Assertion:** the database contains two customers and three purchases.

## Step 5: Query joined sales totals

Send a joined query that filters paid purchases, computes tax-inclusive amount, groups by customer, and
orders by total:

```http
POST http://localhost:5000/v1/insight:query
Content-Type: application/json

{
  "sources": [
    { "alias": "c", "name": "customers" },
    { "alias": "p", "name": "purchases" }
  ],
  "joins": [
    { "left": "c", "right": "p", "kind": 1, "on": { "source": "c.id == p.customer_id", "language": "cel" } }
  ],
  "transformations": [
    { "filter": { "predicate": { "source": "p.status == 'paid'", "language": "cel" } } },
    {
      "compute": {
        "fields": [
          { "alias": "gross", "expression": { "source": "double(p.amount) * 1.1", "language": "cel" } }
        ]
      }
    },
    {
      "group_by": {
        "keys": ["c.full_name"],
        "aggregations": [
          { "field": "gross", "function": 1, "alias": "gross_total" },
          { "field": "p.amount", "function": 5, "alias": "purchase_count" }
        ]
      }
    },
    { "order_by": { "order_by": "gross_total desc" } }
  ]
}
```

**Assertion:** the response has one row for Bob with `gross_total` 220 and one row for Ada with
`gross_total` 110. `purchase_count` is 1 for each row.

## Step 6: Query nested order details

Run a second query for the customer drill-down. Nested selections run against the parent repository
source and can have their own child pipeline:

```http
POST http://localhost:5000/v1/insight:query
Content-Type: application/json

{
  "sources": [{ "alias": "c", "name": "customers" }],
  "selections": [
    { "field": "c.full_name", "alias": "full_name" },
    {
      "field": "c.orders",
      "alias": "paid_orders",
      "transformations": [
        { "filter": { "predicate": { "source": "o.status = 'paid'" } } },
        { "order_by": { "order_by": "o.placed desc" } },
        { "top": { "count": 2 } }
      ],
      "selections": [
        { "field": "o.number", "alias": "number" },
        { "field": "o.amount", "alias": "amount" }
      ]
    }
  ]
}
```

**Assertion:** Ada's `paid_orders` list contains order 1, and Bob's list contains order 3.

## Common pitfalls

**Forgetting the driver registration.** `AddRepositorySource` creates a source binding, but the keyed
`RepositoryDriver` must also be registered with `AddSourceDriver<RepositoryDriver>`.

**Using value expressions with AIP only.** Compute fields need a value-capable language. Use CEL for
computed values or set another value-capable language on the expression slot.

**Adding top or skip as top-level transformations.** Top-level `top` and `skip` transformations are
rejected. Use `page_size`, `skip`, and `page_token` on the request.

**Expecting nested selections after a joined result.** Nested child lists are loaded by the source
driver. Run nested drill-downs against the parent source, and use joined queries for cross-source
summaries.

## See also

- [Insight Guide](../guides/insight.md) — first query and nested selection walkthrough
- [Insight Planning](../documents/insight/planning.md) — plan nodes and validation rules
- [Insight Drivers](../documents/insight/drivers.md) — repository pushdown and residual execution
- [Insight Transports](../documents/insight/transports.md) — HTTP and gRPC query surfaces
