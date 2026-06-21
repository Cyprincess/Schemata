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

public class JoinExecutionShould
{
    [Fact]
    public async Task Inner_Joins_Two_Sources_And_Projects_Both() {
        var (executor, builder) = Setup();

        var request = JoinRequest();
        request.Selections.Add(new() { Field = "b.full_name", Alias = "full_name" });
        request.Selections.Add(new() { Field = "p.amount", Alias = "amount" });

        var plan   = await builder.BuildAsync(request, CancellationToken.None);
        var result = await executor.ExecuteAsync(plan, request, null, CancellationToken.None);

        Assert.Equal(3, result.Rows.Count);
        Assert.Equal(3, result.TotalSize);
        Assert.Contains(result.Rows, r => (string?)r["full_name"] == "Ada" && Convert.ToInt32(r["amount"]) == 100);
        Assert.Contains(result.Rows, r => (string?)r["full_name"] == "Bob" && Convert.ToInt32(r["amount"]) == 200);
    }

    [Fact]
    public async Task Filters_After_A_Join() {
        var (executor, builder) = Setup();

        var request = JoinRequest();
        request.Transformations.Add(new() { Filter = new(new InsightExpression("p.amount > 75", "cel")) });
        request.Selections.Add(new() { Field = "p.amount", Alias = "amount" });

        var plan   = await builder.BuildAsync(request, CancellationToken.None);
        var result = await executor.ExecuteAsync(plan, request, null, CancellationToken.None);

        Assert.Equal(2, result.Rows.Count);
        Assert.All(result.Rows, r => Assert.True(Convert.ToInt32(r["amount"]) > 75));
    }

    [Fact]
    public async Task Groups_And_Aggregates_After_A_Join() {
        var (executor, builder) = Setup();

        var request = JoinRequest();
        request.Transformations.Add(new() {
            GroupBy = new(["b.full_name"], [new("p.amount", AggregationFunction.Sum, "spent")]),
        });
        request.Transformations.Add(new() { OrderBy = new("spent desc") });

        var plan   = await builder.BuildAsync(request, CancellationToken.None);
        var result = await executor.ExecuteAsync(plan, request, null, CancellationToken.None);

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("Bob", result.Rows[0]["full_name"]);
        Assert.Equal(200d, result.Rows[0]["spent"]);
        Assert.Equal("Ada", result.Rows[1]["full_name"]);
        Assert.Equal(150d, result.Rows[1]["spent"]);
    }

    private static QueryInsightRequest JoinRequest() {
        return new QueryInsightRequest {
            Sources = { new("b", "buyers"), new("p", "purchases") },
            Joins   = { new("b", "p", JoinKind.Inner, new InsightExpression("b.id == p.buyer_id", "cel")) },
        };
    }

    private static (PlanExecutor Executor, InsightPlanBuilder Builder) Setup() {
        var buyers = new List<Buyer> {
            new() { Id = 1, FullName = "Ada", Name = "ada" },
            new() { Id = 2, FullName = "Bob", Name = "bob" },
        };
        var purchases = new List<Purchase> {
            new() { BuyerId = 1, Amount = 100, Name = "p1" },
            new() { BuyerId = 1, Amount = 50, Name = "p2" },
            new() { BuyerId = 2, Amount = 200, Name = "p3" },
        };

        var services = new ServiceCollection();
        services.AddAipExpressions();
        services.AddCelExpressions();
        services.AddOrderExpressions();
        services.AddKeyedSingleton<ISourceDriver, RepositoryDriver>(RepositoryDriver.DriverName);
        services.AddSingleton(Repository(buyers));
        services.AddSingleton(Repository(purchases));

        var catalog = new InMemoryInsightSourceCatalog(new Dictionary<string, SourceConfig> {
            ["buyers"]    = new(RepositoryDriver.DriverName, new Dictionary<string, object?> { ["resource"] = "buyers" }),
            ["purchases"] = new(RepositoryDriver.DriverName, new Dictionary<string, object?> { ["resource"] = "purchases" }),
        });
        services.AddSingleton<IInsightSourceCatalog>(catalog);

        var options = Options.Create(new SchemataInsightOptions());
        var sp      = services.BuildServiceProvider();

        return (new PlanExecutor(sp, new LocalPipelineExecutor(sp), options),
                new InsightPlanBuilder([catalog], sp, options));
    }

    private static IRepository<T> Repository<T>(List<T> data)
        where T : class {
        var repository = new Mock<IRepository<T>>();
        repository
           .Setup(r => r.ListAsync(It.IsAny<Func<IQueryable<T>, IQueryable<T>>>(), It.IsAny<CancellationToken>()))
           .Returns((Func<IQueryable<T>, IQueryable<T>> q, CancellationToken ct) => AsyncList(q(data.AsQueryable()), ct));
        return repository.Object;
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

    [CanonicalName("buyers/{buyer}")]
    public sealed class Buyer : ICanonicalName
    {
        public int     Id       { get; set; }
        public string? FullName { get; set; }

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    [CanonicalName("purchases/{purchase}")]
    public sealed class Purchase : ICanonicalName
    {
        public int BuyerId { get; set; }
        public int Amount  { get; set; }

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }
}
