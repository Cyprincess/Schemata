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
using Schemata.Core;
using Schemata.Entity.Repository;
using Schemata.Expressions.Aip;
using Schemata.Expressions.Order;
using Schemata.Expressions.Skeleton;
using Schemata.Insight.Foundation;
using Schemata.Insight.Skeleton;
using Xunit;
using Student = Schemata.Insight.Tests.RepositoryDriverShould.Student;

namespace Schemata.Insight.Tests;

public class SchemataInsightBuilderShould
{
    [Fact]
    public void Binds_Default_Language_To_The_First_Enabled_Language() {
        var services = new ServiceCollection();
        var builder  = new SchemataInsightBuilder(new SchemataOptions(), services);

        builder.UseAip();

        var options = Resolve(services).GetRequiredService<IOptions<SchemataInsightOptions>>().Value;
        Assert.Equal(ExpressionLanguages.Aip, options.DefaultLanguage);
    }

    [Fact]
    public void Lets_An_Explicit_Default_Language_Override_The_Profile() {
        var services = new ServiceCollection();
        var builder  = new SchemataInsightBuilder(new SchemataOptions(), services);

        builder.UseAip().DefaultLanguage(ExpressionLanguages.Cel);

        var options = Resolve(services).GetRequiredService<IOptions<SchemataInsightOptions>>().Value;
        Assert.Equal(ExpressionLanguages.Cel, options.DefaultLanguage);
    }

    [Fact]
    public void Accumulates_Sources_And_The_Total_Size_Mode() {
        var services = new ServiceCollection();
        var builder  = new SchemataInsightBuilder(new SchemataOptions(), services);

        builder.WithTotalSize(TotalSizeMode.None).AddRepositorySource("students", "students");

        var options = Resolve(services).GetRequiredService<IOptions<SchemataInsightOptions>>().Value;
        Assert.Equal(TotalSizeMode.None, options.TotalSize);
        var config = Assert.Contains("students", (IDictionary<string, SourceConfig>)options.Sources);
        Assert.Equal(RepositoryDriver.DriverName, config.DriverName);
        Assert.Equal("students", config.Params["resource"]);
    }

    [Fact]
    public async Task Resolves_A_Query_Service_That_Runs_End_To_End() {
        var students = Enumerable.Range(0, 3)
                                 .Select(i => new Student { Age = 20 + i, FullName = $"S{i}", Name = $"s{i}" })
                                 .ToList();

        var services = new ServiceCollection();
        var builder  = new SchemataInsightBuilder(new SchemataOptions(), services);
        builder.UseAip().UseOrdering();
        builder.AddRepositorySource("students", "students")
               .AddSourceDriver<RepositoryDriver>(RepositoryDriver.DriverName);
        RegisterPipeline(services);
        services.AddSingleton(StudentRepository(students));

        var provider = Resolve(services);
        var service  = provider.GetRequiredService<IInsightService>();

        var request = new QueryInsightRequest { Sources = { new("c", "students") }, PageSize = 2 };
        var result  = await service.QueryAsync(request, null, CancellationToken.None);

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(3, result.TotalSize);
        Assert.NotNull(result.NextPageToken);
    }

    private static void RegisterPipeline(IServiceCollection services) {
        services.AddSingleton<IInsightSourceCatalog>(sp => {
            var options = sp.GetRequiredService<IOptions<SchemataInsightOptions>>().Value;
            return new InMemoryInsightSourceCatalog(new Dictionary<string, SourceConfig>(options.Sources));
        });
        services.AddSingleton<InsightPlanBuilder>();
        services.AddSingleton<LocalPipelineExecutor>();
        services.AddSingleton<PlanExecutor>();
        services.AddSingleton<IInsightService, DefaultInsightService>();
    }

    private static IRepository<Student> StudentRepository(List<Student> students) {
        var repository = new Mock<IRepository<Student>>();
        repository
           .Setup(r => r.ListAsync(It.IsAny<Func<IQueryable<Student>, IQueryable<Student>>>(),
                                   It.IsAny<CancellationToken>()))
           .Returns((Func<IQueryable<Student>, IQueryable<Student>> q, CancellationToken ct)
                        => AsyncList(q(students.AsQueryable()), ct));
        return repository.Object;
    }

    private static ServiceProvider Resolve(IServiceCollection services) {
        return services.BuildServiceProvider();
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
