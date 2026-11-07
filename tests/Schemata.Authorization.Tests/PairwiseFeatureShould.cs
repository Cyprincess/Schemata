using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Features;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Core.Building;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.LinqToDB;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Resource.Foundation;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class PairwiseFeatureShould
{
    [Fact]
    public void Register_The_Pairwise_Machinery() {
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(new SchemataAuthorizationOptions()));
        new PairwiseSubjectsFeature<SchemataApplication, SchemataSubjectMapping>().ConfigureServices(services, new(), new());

        Assert.Contains(services, d => d.ServiceType == typeof(IClaimsAdvisor)
                                   && d.ImplementationType == typeof(AdviceClaimsPairwise<SchemataApplication, SchemataSubjectMapping>));
        Assert.Contains(services, d => d.ServiceType == typeof(PairwiseSubjectTranslator<SchemataApplication, SchemataSubjectMapping>));
        Assert.Contains(services, d => d.ServiceType == typeof(IPairwiseSubjectTranslator));

        using var provider = services.BuildServiceProvider();
        Assert.Contains(
            provider.GetRequiredService<IEnumerable<IDiscoveryAdvisor>>(),
            advisor => advisor is AdviceDiscoveryPairwise);
    }

    [Fact]
    public async Task Advertise_Pairwise_Discovery_When_A_Salt_Is_Configured() {
        var discovery = await Advise_Discovery(new() { PairwiseSalt = "salt" });

        Assert.Equal([SubjectTypes.Public, SubjectTypes.Pairwise], discovery.Document!.SubjectTypesSupported);
    }

    [Fact]
    public async Task Keep_The_Discovery_Document_Public_Only_Without_A_Salt() {
        var discovery = await Advise_Discovery(new());

        Assert.Equal([SubjectTypes.Public], discovery.Document!.SubjectTypesSupported);
    }

    private static async Task<DiscoveryContext> Advise_Discovery(SchemataAuthorizationOptions options) {
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(options));
        new PairwiseSubjectsFeature<SchemataApplication, SchemataSubjectMapping>().ConfigureServices(services, new(), new());
        using var provider = services.BuildServiceProvider();

        // Mirrors the DiscoveryHandler baseline: public subjects before the feature advisors run.
        var discovery = new DiscoveryContext {
            Document = new() { SubjectTypesSupported = [SubjectTypes.Public] },
        };

        foreach (var advisor in provider.GetRequiredService<IEnumerable<IDiscoveryAdvisor>>()) {
            await advisor.AdviseAsync(new(provider), discovery, CancellationToken.None);
        }

        return discovery;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentForwardMappings_LeaveOneStableRow(bool linq) {
        await using var host = await MappingHost.CreateAsync(linq, sharedPairwise: false);
        var application = Application();

        var attempts = await Task.WhenAll(
            EnsureMappingAsync(host, application, "users/alice"),
            EnsureMappingAsync(host, application, "users/alice"));

        Assert.All(attempts, attempt => AssertAllowedConflict(attempt.Error));
        Assert.All(attempts, attempt => Assert.Equal(attempts[0].Value, attempt.Value));
        var pairwise = attempts[0].Value;
        Assert.NotNull(pairwise);
        var row = Assert.Single(await host.RowsAsync());
        Assert.Equal("users/alice", row.Subject);
        Assert.Equal(pairwise, row.PairwiseSubject);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentReverseMappings_LeaveOneStableRow(bool linq) {
        await using var host = await MappingHost.CreateAsync(linq, sharedPairwise: true);
        var application = Application();

        var attempts = await Task.WhenAll(
            EnsureMappingAsync(host, application, "users/alice"),
            EnsureMappingAsync(host, application, "users/bob"));

        Assert.All(attempts, attempt => AssertAllowedConflict(attempt.Error));
        Assert.Contains(attempts, attempt => attempt.Error is AlreadyExistsException or AbortedException);
        var row = Assert.Single(await host.RowsAsync());
        Assert.Equal("shared-pairwise", row.PairwiseSubject);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentMappingRepairs_LeaveOneCompleteRow(bool linq) {
        await using var host = await MappingHost.CreateAsync(linq, sharedPairwise: false);
        var application = Application();
        await host.SeedAsync(new() {
            Application = application.CanonicalName,
            Subject = "users/alice",
        });

        var attempts = await Task.WhenAll(
            EnsureMappingAsync(host, application, "users/alice"),
            EnsureMappingAsync(host, application, "users/alice"));

        Assert.All(attempts, attempt => AssertAllowedConflict(attempt.Error));
        var row = Assert.Single(await host.RowsAsync());
        Assert.False(string.IsNullOrWhiteSpace(row.PairwiseSubject));
    }

    private static SchemataApplication Application() {
        return new() {
            ClientId = "client-1",
            CanonicalName = "applications/client-1",
            SectorIdentifierUri = "https://rp.example",
        };
    }

    private static async Task<(string? Value, Exception? Error)> EnsureMappingAsync(
        MappingHost host,
        SchemataApplication application,
        string subject) {
        await using var scope = host.Services.CreateAsyncScope();
        var translator = scope.ServiceProvider.GetRequiredService<PairwiseSubjectTranslator<SchemataApplication, SchemataSubjectMapping>>();
        try {
            return (await translator.EnsureMappingAsync(application, subject, CancellationToken.None), null);
        } catch (Exception exception) {
            return (null, exception);
        }
    }

    private static void AssertAllowedConflict(Exception? error) {
        Assert.True(error is null or AlreadyExistsException or AbortedException, error?.ToString());
    }

    private sealed class MappingNameAdvisor : IRepositoryAddAdvisor<SchemataSubjectMapping>
    {
        public int Order => AdviceAddCanonicalName.DefaultOrder - 1;

        public Task<AdviseResult> AdviseAsync(
            AdviceContext context,
            IRepository<SchemataSubjectMapping> repository,
            SchemataSubjectMapping entity,
            MutationOptions options,
            CancellationToken ct) {
            entity.Name ??= Guid.NewGuid().ToString("n");
            return Task.FromResult(AdviseResult.Continue);
        }
    }

    private sealed class MappingContext(DbContextOptions<MappingContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model) { model.Entity<SchemataSubjectMapping>(); }
    }

    private sealed class MappingConnection(DataOptions options) : DataConnection(options);

    private sealed class MappingHost(ServiceProvider services, string path) : IAsyncDisposable
    {
        public ServiceProvider Services => services;

        public static async Task<MappingHost> CreateAsync(bool linq, bool sharedPairwise) {
            var path = Path.Combine(Path.GetTempPath(), $"schemata-pairwise-race-{Guid.NewGuid():N}.db");
            var services = new ServiceCollection();
            if (linq) {
                var schema = new MappingSchema();
                schema.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());
                var options = new DataOptions().UseSQLite($"Data Source={path};Pooling=False").UseMappingSchema(schema);
                services.AddSingleton<Func<MappingConnection>>(_ => () => new(options));
                services.AddRepository<SchemataSubjectMapping, LinqToDbRepository<MappingConnection, SchemataSubjectMapping>>();
                using var db = new MappingConnection(options);
                db.CreateTable<SchemataSubjectMapping>();
            } else {
                services.AddDbContextFactory<MappingContext>(options => options.UseSqlite($"Data Source={path};Pooling=False")
                    .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
                services.AddRepository<SchemataSubjectMapping, EfCoreRepository<MappingContext, SchemataSubjectMapping>>();
            }

            var registry = new ResourceRegistry();
            registry.Add(new(typeof(SchemataApplication)), []);
            registry.Add(new(typeof(SchemataSubjectMapping)), []);
            services.TryAddSingleton(registry);
            services.TryAddSingleton<IResourceTypeResolver, DefaultResourceTypeResolver>();
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataSubjectMapping>, MappingNameAdvisor>());

            var apps = new Mock<IApplicationManager<SchemataApplication>>();
            apps.Setup(manager => manager.EnlistPublicationAsync(It.IsAny<IUnitOfWork>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            services.AddSingleton(apps.Object);
            if (sharedPairwise) {
                var subjects = new Mock<ISubjectIdentifierService>();
                subjects.Setup(service => service.Resolve(It.IsAny<string>(), It.IsAny<SchemataApplication>()))
                        .Returns("shared-pairwise");
                services.AddSingleton(subjects.Object);
            } else {
                services.AddSingleton<ISubjectIdentifierService>(new SubjectIdentifierService(
                    Options.Create(new SchemataAuthorizationOptions {
                        SubjectType = SubjectTypes.Pairwise,
                        PairwiseSalt = "test-salt",
                    })));
            }
            services.AddScoped<PairwiseSubjectTranslator<SchemataApplication, SchemataSubjectMapping>>();
            var root = services.BuildServiceProvider();
            if (!linq) {
                await using var db = await root.GetRequiredService<IDbContextFactory<MappingContext>>().CreateDbContextAsync();
                await db.Database.EnsureCreatedAsync();
            }
            return new(root, path);
        }

        public async Task SeedAsync(SchemataSubjectMapping row) {
            await using var scope = Services.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataSubjectMapping>>();
            await repository.AddAsync(row);
            await repository.CommitAsync();
        }

        public async Task<List<SchemataSubjectMapping>> RowsAsync() {
            await using var scope = Services.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataSubjectMapping>>();
            return await repository.ListAsync(query => query).ToListAsync();
        }

        public async ValueTask DisposeAsync() {
            await services.DisposeAsync();
            File.Delete(path);
        }
    }
}
