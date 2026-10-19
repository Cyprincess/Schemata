using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Insight.Foundation.Catalog;
using Schemata.Insight.Skeleton.Entities;
using Xunit;

namespace Schemata.Insight.Tests;

[Trait("Layer", "Unit")]
public sealed class InsightSourceCatalogShould
{
    [Fact]
    public async Task Resolve_Keeps_Integer_Source_Parameters_Exact() {
        var record = new SchemataInsightSource {
            Name   = "orders",
            Driver = "probe",
            Params = "{\"id\":9007199254740993,\"rate\":0.5}",
        };
        var repository = new Mock<IRepository<SchemataInsightSource>>();
        repository.Setup(value => value.FirstOrDefaultAsync(
                      It.IsAny<Func<IQueryable<SchemataInsightSource>, IQueryable<SchemataInsightSource>>>(),
                      It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                  .Returns((Func<IQueryable<SchemataInsightSource>, IQueryable<SchemataInsightSource>> query,
                            CancellationToken _, QueryOptions _) =>
                      ValueTask.FromResult<SchemataInsightSource?>(query(new[] { record }.AsQueryable()).FirstOrDefault()));
        var services = new ServiceCollection();
        services.AddScoped(_ => repository.Object);
        using var provider = services.BuildServiceProvider();

        var catalog = new DatabaseInsightSourceCatalog<SchemataInsightSource>(provider.GetRequiredService<IServiceScopeFactory>());
        var config  = await catalog.ResolveAsync("orders", CancellationToken.None);

        Assert.Equal(9007199254740993L, Assert.IsType<long>(config!.Params["id"]));
        Assert.Equal(0.5, Assert.IsType<double>(config.Params["rate"]));
    }
}
