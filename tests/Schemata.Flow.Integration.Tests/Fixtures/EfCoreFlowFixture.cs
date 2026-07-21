using System.IO;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Common;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Entities;
using Xunit;

namespace Schemata.Flow.Integration.Tests.Fixtures;

public sealed class EfCoreFlowFixture : IAsyncLifetime, IFlowIntegrationFixture
{
    private readonly string _dbPath = $"{Identifiers.NewUid():n}.db";

    private ServiceProvider? _root;

    public SchemataFlowOptions FlowOptions { get; } = new();

    #region IAsyncLifetime Members

    public async Task InitializeAsync() {
        var services = new ServiceCollection();
        services.AddDbContextFactory<TestDbContext>(options => options.UseSqlite($"Data Source={_dbPath}")
                                                               .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<Order, EfCoreRepository<TestDbContext, Order>>();
        services.AddRepository<SchemataProcess, EfCoreRepository<TestDbContext, SchemataProcess>>();
        services.AddRepository<SchemataProcessToken, EfCoreRepository<TestDbContext, SchemataProcessToken>>();
        services.AddRepository<SchemataProcessTransition, EfCoreRepository<TestDbContext, SchemataProcessTransition>>();
        services.AddRepository<SchemataProcessSource, EfCoreRepository<TestDbContext, SchemataProcessSource>>();
        services.AddRepository<SchemataProcessCompensation, EfCoreRepository<TestDbContext, SchemataProcessCompensation>>();
        services.AddScoped<IUnitOfWork<TestDbContext>, EfCoreUnitOfWork<TestDbContext>>();
        FlowFixtureServices.AddResourceTypeResolver(
            services, typeof(Order), typeof(SchemataProcess), typeof(SchemataProcessToken));
        FlowFixtureServices.AddFlowServices(services);
        services.AddSingleton<IOptions<SchemataFlowOptions>>(Options.Create(FlowOptions));

        _root = services.BuildServiceProvider();

        using (var scope = _root.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        await FlowFixtureServices.RegisterProcessesAsync(_root);
    }

    public async Task DisposeAsync() {
        if (_root is not null) {
            using var scope = _root.CreateScope();
            var       db    = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await db.Database.EnsureDeletedAsync();
            await _root.DisposeAsync();
        }

        if (File.Exists(_dbPath)) {
            File.Delete(_dbPath);
        }
    }

    #endregion

    public IServiceScope CreateScope() { return _root!.CreateScope(); }
}
