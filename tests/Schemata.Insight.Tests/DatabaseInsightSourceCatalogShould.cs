using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Insight.Foundation;
using Schemata.Insight.Skeleton;
using Xunit;

namespace Schemata.Insight.Tests;

public class DatabaseInsightSourceCatalogShould
{
    [Fact]
    public async Task Resolves_A_Stored_Source_With_Parsed_Params() {
        var catalog = Catalog(
            new SchemataInsightSource {
                Name   = "live",
                Driver = "repository",
                Params = """{"resource":"customers"}""",
            });

        var config = await catalog.ResolveAsync("live", CancellationToken.None);

        Assert.NotNull(config);
        Assert.Equal("repository", config.DriverName);
        Assert.Equal("customers", Assert.IsType<string>(config.Params["resource"]));
    }

    [Fact]
    public async Task Returns_Null_For_An_Unknown_Source() {
        var catalog = Catalog();

        Assert.Null(await catalog.ResolveAsync("missing", CancellationToken.None));
    }

    [Fact]
    public async Task Lists_Stored_Source_Names() {
        var catalog = Catalog(
            new SchemataInsightSource { Name = "a", Driver = "repository" },
            new SchemataInsightSource { Name = "b", Driver = "repository" });

        var names = await catalog.ListNamesAsync(CancellationToken.None);

        Assert.Equal(["a", "b"], names.OrderBy(n => n));
    }

    [Fact]
    public async Task Resolution_Prefers_The_First_Catalog() {
        var config = await Resolve([Memory(("x", "first")), Memory(("x", "second"))], "x");

        Assert.Equal("first", config.DriverName);
    }

    [Fact]
    public async Task Resolution_Falls_Back_To_The_Next_Catalog() {
        var config = await Resolve([Memory(), Memory(("x", "second"))], "x");

        Assert.Equal("second", config.DriverName);
    }

    private static InMemoryInsightSourceCatalog Memory(params (string Name, string Driver)[] sources) {
        return new InMemoryInsightSourceCatalog(
            sources.ToDictionary(s => s.Name, s => new SourceConfig(s.Driver, Empty())));
    }

    private static async Task<SourceConfig> Resolve(IEnumerable<IInsightSourceCatalog> catalogs, string name) {
        var builder = new InsightPlanBuilder(catalogs, new ServiceCollection().BuildServiceProvider(),
                                             Options.Create(new SchemataInsightOptions()));
        var plan = await builder.BuildAsync(new QueryInsightRequest { Sources = { new("s", name) } },
                                            CancellationToken.None);

        var selection = Assert.IsType<SelectionNode>(Assert.IsType<LimitNode>(plan).Input);
        return Assert.IsType<SourceNode>(selection.Input).Config;
    }

    private static IReadOnlyDictionary<string, object?> Empty() {
        return new Dictionary<string, object?>();
    }

    private static DatabaseInsightSourceCatalog Catalog(params SchemataInsightSource[] sources) {
        var repository = new Mock<IRepository<SchemataInsightSource>>();
        repository
           .Setup(r => r.FirstOrDefaultAsync(
                       It.IsAny<Func<IQueryable<SchemataInsightSource>, IQueryable<SchemataInsightSource>>>(),
                       It.IsAny<CancellationToken>()))
           .Returns((Func<IQueryable<SchemataInsightSource>, IQueryable<SchemataInsightSource>> q, CancellationToken _)
                        => new ValueTask<SchemataInsightSource?>(q(sources.AsQueryable()).FirstOrDefault()));
        repository
           .Setup(r => r.ListAsync(
                       It.IsAny<Func<IQueryable<SchemataInsightSource>, IQueryable<SchemataInsightSource>>?>(),
                       It.IsAny<CancellationToken>()))
           .Returns((Func<IQueryable<SchemataInsightSource>, IQueryable<SchemataInsightSource>>? q, CancellationToken ct)
                        => AsyncList(q is null ? sources.AsQueryable() : q(sources.AsQueryable()), ct));

        var services = new ServiceCollection();
        services.AddSingleton(repository.Object);
        var provider = services.BuildServiceProvider();

        return new DatabaseInsightSourceCatalog(provider.GetRequiredService<IServiceScopeFactory>());
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
