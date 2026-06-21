using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Resource;
using Schemata.Entity.Repository;
using Schemata.Expressions.Aip;
using Schemata.Expressions.Order;
using Schemata.Insight.Foundation;
using Schemata.Insight.Skeleton;
using Xunit;
using Student = Schemata.Insight.Tests.RepositoryDriverShould.Student;

namespace Schemata.Insight.Tests;

public class PlanExecutorShould
{
    [Fact]
    public async Task Pages_Rows_And_Emits_A_Next_Token() {
        var (executor, builder) = Setup(Students(5), TotalSizeMode.Exact);

        var request = new QueryInsightRequest { Sources = { new("c", "students") }, PageSize = 2 };
        var plan    = await builder.BuildAsync(request, CancellationToken.None);
        var page1   = await executor.ExecuteAsync(plan, request, null, CancellationToken.None);

        Assert.Equal(2, page1.Rows.Count);
        Assert.NotNull(page1.NextPageToken);
        Assert.Equal(5, page1.TotalSize);

        var next     = new QueryInsightRequest { Sources = { new("c", "students") }, PageSize = 2, PageToken = page1.NextPageToken };
        var nextPlan = await builder.BuildAsync(next, CancellationToken.None);
        var page2    = await executor.ExecuteAsync(nextPlan, next, null, CancellationToken.None);

        Assert.Equal(2, page2.Rows.Count);
        Assert.NotEqual(page1.Rows[0]["name"], page2.Rows[0]["name"]);
    }

    [Fact]
    public async Task Omits_Total_Size_When_Mode_Is_None() {
        var (executor, builder) = Setup(Students(3), TotalSizeMode.None);

        var request = new QueryInsightRequest { Sources = { new("c", "students") } };
        var plan    = await builder.BuildAsync(request, CancellationToken.None);
        var result  = await executor.ExecuteAsync(plan, request, null, CancellationToken.None);

        Assert.Null(result.TotalSize);
        Assert.Equal(3, result.Rows.Count);
    }

    [Fact]
    public async Task Reports_Exact_Total_Across_A_Page() {
        var (executor, builder) = Setup(Students(4), TotalSizeMode.Exact);

        var request = new QueryInsightRequest { Sources = { new("c", "students") }, PageSize = 2 };
        var plan    = await builder.BuildAsync(request, CancellationToken.None);
        var result  = await executor.ExecuteAsync(plan, request, null, CancellationToken.None);

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(4, result.TotalSize);
    }

    [Fact]
    public async Task Routes_GroupBy_Through_The_Local_Pipeline() {
        var (executor, builder) = Setup(GroupableStudents(), TotalSizeMode.Exact);

        var result = await RunGroupBy(executor, builder);

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(20, result.Rows[0]["age"]);
        Assert.Equal(2, result.Rows[0]["headcount"]);
        Assert.Equal(30, result.Rows[1]["age"]);
        Assert.Equal(1, result.Rows[1]["headcount"]);
    }

    [Fact]
    public async Task Reports_Exact_Total_As_The_Final_Grouped_Count() {
        var (executor, builder) = Setup(GroupableStudents(), TotalSizeMode.Exact);

        var result = await RunGroupBy(executor, builder);

        Assert.Equal(2, result.TotalSize);
    }

    [Fact]
    public async Task Reports_Estimated_Total_As_The_Pushed_Superset_Upper_Bound() {
        var (executor, builder) = Setup(GroupableStudents(), TotalSizeMode.Estimated);

        var result = await RunGroupBy(executor, builder);

        Assert.Equal(3, result.TotalSize);
        Assert.True(result.TotalSize >= result.Rows.Count);
    }

    [Fact]
    public async Task Omits_Total_For_A_Grouped_Query_When_Mode_Is_None() {
        var (executor, builder) = Setup(GroupableStudents(), TotalSizeMode.None);

        var result = await RunGroupBy(executor, builder);

        Assert.Null(result.TotalSize);
    }

    private static List<Student> GroupableStudents() {
        return [
            new() { Age = 20, FullName = "A", Name = "a" },
            new() { Age = 20, FullName = "B", Name = "b" },
            new() { Age = 30, FullName = "C", Name = "c" },
        ];
    }

    private static async Task<QueryInsightResponse> RunGroupBy(PlanExecutor executor, InsightPlanBuilder builder) {
        var request = new QueryInsightRequest {
            Sources = { new("c", "students") },
            Transformations = {
                new() {
                    GroupBy = new(["c.age"], [new("c.age", AggregationFunction.Count, "headcount")]),
                },
                new() { OrderBy = new("age asc") },
            },
        };

        var plan = await builder.BuildAsync(request, CancellationToken.None);
        return await executor.ExecuteAsync(plan, request, null, CancellationToken.None);
    }

    private static List<Student> Students(int count) {
        return Enumerable.Range(0, count)
                         .Select(i => new Student {
                             Age           = 18 + i,
                             FullName      = $"Student {i}",
                             Name          = $"student-{i}",
                             CanonicalName = $"students/student-{i}",
                         })
                         .ToList();
    }

    private static (PlanExecutor Executor, InsightPlanBuilder Builder) Setup(
        List<Student> students,
        TotalSizeMode total
    ) {
        var repository = new Mock<IRepository<Student>>();
        repository
           .Setup(r => r.ListAsync(It.IsAny<Func<IQueryable<Student>, IQueryable<Student>>>(),
                                   It.IsAny<CancellationToken>()))
           .Returns((Func<IQueryable<Student>, IQueryable<Student>> q, CancellationToken ct)
                        => AsyncList(q(students.AsQueryable()), ct));

        var services = new ServiceCollection();
        services.AddAipExpressions();
        services.AddOrderExpressions();
        services.AddKeyedSingleton<ISourceDriver, RepositoryDriver>("repository");
        services.AddSingleton(repository.Object);

        var catalog = new InMemoryInsightSourceCatalog(new Dictionary<string, SourceConfig> {
            ["students"] = new("repository", new Dictionary<string, object?> { ["resource"] = "students" }),
        });
        services.AddSingleton<IInsightSourceCatalog>(catalog);

        var options = Options.Create(new SchemataInsightOptions { TotalSize = total });
        services.AddSingleton(options);

        var sp = services.BuildServiceProvider();
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
}
