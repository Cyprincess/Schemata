using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Entity.Repository;
using Schemata.Expressions.Aip;
using Schemata.Expressions.Cel;
using Schemata.Expressions.Order;
using Schemata.Insight.Foundation;
using Schemata.Insight.Skeleton;
using Xunit;

namespace Schemata.Insight.Tests;

public class NestedSelectionShould
{
    [Fact]
    public async Task Projects_A_Filtered_Ordered_Topped_Computed_Child_List() {
        var customers = new List<Customer> {
            new() {
                Name = "ada", FullName = "Ada",
                Orders = [
                    new() { Id = 1, Status = "paid", Amount = 100, Placed = 3 },
                    new() { Id = 2, Status = "open", Amount = 999, Placed = 2 },
                    new() { Id = 3, Status = "paid", Amount = 200, Placed = 5 },
                    new() { Id = 4, Status = "paid", Amount = 50, Placed = 1 },
                ],
            },
        };

        var (executor, builder) = Setup(customers);

        var request = new QueryInsightRequest {
            Sources = { new("c", "customers") },
            Selections = {
                new() { Field = "c.full_name", Alias = "full_name" },
                new() {
                    Field = "c.orders",
                    Alias = "recent_paid_orders",
                    Transformations = {
                        new() { Filter  = new(new InsightExpression("o.status = 'paid'")) },
                        new() { OrderBy = new("o.placed desc") },
                        new() { Top     = new(2) },
                    },
                    Selections = {
                        new() { Field = "o.id", Alias = "id" },
                        new() { Expression = new InsightExpression("o.amount * 1.1", "cel"), Alias = "total" },
                    },
                },
            },
        };

        var plan   = await builder.BuildAsync(request, CancellationToken.None);
        var result = await executor.ExecuteAsync(plan, request, null, CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal("Ada", row["full_name"]);

        var orders = Assert.IsAssignableFrom<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(row["recent_paid_orders"]);
        Assert.Equal(2, orders.Count);
        Assert.Equal(3, orders[0]["id"]);
        Assert.Equal(220d, Convert.ToDouble(orders[0]["total"]), 6);
        Assert.Equal(1, orders[1]["id"]);
        Assert.Equal(110d, Convert.ToDouble(orders[1]["total"]), 6);
    }

    [Fact]
    public async Task Describes_A_Nested_Field_As_A_List_Of_Objects() {
        var (executor, builder) = Setup([new() { Name = "ada", FullName = "Ada", Orders = [] }]);

        var request = new QueryInsightRequest {
            Sources = { new("c", "customers") },
            Selections = {
                new() {
                    Field = "c.orders",
                    Alias = "orders",
                    Selections = { new() { Field = "o.id", Alias = "id" } },
                },
            },
        };

        var plan   = await builder.BuildAsync(request, CancellationToken.None);
        var result = await executor.ExecuteAsync(plan, request, null, CancellationToken.None);

        var nested = Assert.Single(result.Schema, field => field.Name == "orders");
        Assert.True(nested.IsList);
        Assert.Equal(FieldType.Object, nested.Type);
        Assert.Contains(nested.Children, child => child.Name == "id");
    }

    private static (PlanExecutor Executor, InsightPlanBuilder Builder) Setup(List<Customer> customers) {
        var repository = new Mock<IRepository<Customer>>();
        repository
           .Setup(r => r.ListAsync(It.IsAny<Func<IQueryable<Customer>, IQueryable<Customer>>>(),
                                   It.IsAny<CancellationToken>()))
           .Returns((Func<IQueryable<Customer>, IQueryable<Customer>> q, CancellationToken ct)
                        => AsyncList(q(customers.AsQueryable()), ct));

        var services = new ServiceCollection();
        services.AddAipExpressions();
        services.AddCelExpressions();
        services.AddOrderExpressions();
        services.AddKeyedSingleton<ISourceDriver, RepositoryDriver>(RepositoryDriver.DriverName);
        services.AddSingleton(repository.Object);

        var catalog = new InMemoryInsightSourceCatalog(new Dictionary<string, SourceConfig> {
            ["customers"] = new(RepositoryDriver.DriverName, new Dictionary<string, object?> { ["resource"] = "customers" }),
        });
        services.AddSingleton<IInsightSourceCatalog>(catalog);

        var options = Options.Create(new SchemataInsightOptions());
        var sp      = services.BuildServiceProvider();

        return (new PlanExecutor(sp, new LocalPipelineExecutor(sp), options),
                new InsightPlanBuilder([catalog], sp, options));
    }

    private static async IAsyncEnumerable<T> AsyncList<T>(
        IQueryable<T>                              source,
        [EnumeratorCancellation] CancellationToken ct
    ) {
        foreach (var item in source) {
            ct.ThrowIfCancellationRequested();
            yield return await Task.FromResult(item);
        }
    }

    [CanonicalName("customers/{customer}")]
    public sealed class Customer : ICanonicalName
    {
        public string?     FullName { get; set; }
        public List<Order> Orders   { get; set; } = [];

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class Order
    {
        public int     Id     { get; set; }
        public string? Status { get; set; }
        public int     Amount { get; set; }
        public int     Placed { get; set; }
    }
}
