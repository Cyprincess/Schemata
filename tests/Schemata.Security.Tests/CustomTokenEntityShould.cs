using System;
using System.IO;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.LinqToDB;
using Schemata.Entity.Repository;
using Schemata.Security.Foundation.Features;
using Schemata.Security.Foundation.Stores;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Security.Skeleton.SecurityConstants;

namespace Schemata.Security.Tests;

/// <summary>
///     Proves a host-selected token entity end to end: <c>UseSecurity&lt;TSecurity, TToken&gt;</c>
///     registers the closed store, and publication, redemption, rotation, and revocation persist
///     <see cref="MyToken" /> rows on both repository providers.
/// </summary>
[Trait("Category", "Integration")]
public class CustomTokenEntityShould
{
    public sealed class MyToken : SchemataToken;

    public sealed class MySecurity : SchemataSecurity;

    public sealed class CustomContext(DbContextOptions<CustomContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model) {
            model.Entity<MyToken>();
            model.Entity<MySecurity>();
        }
    }

    public sealed class CustomConnection(DataOptions options) : DataConnection(options);

    [Fact]
    public void Register_Only_The_Host_Token_Store_Through_UseSecurity() {
        var builder = new SchemataBuilder(new ConfigurationBuilder().Build(), null!);
        builder.UseSecurity<MySecurity, MyToken>();

        Assert.True(builder.HasFeature<SchemataSecurityFeature<MySecurity, MyToken>>());

        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        new SchemataSecurityFeature<MySecurity, MyToken>()
            .ConfigureServices(services, builder.Options, builder.Configurators, builder.Configuration, null!);

        using var provider = services.BuildServiceProvider();

        Assert.IsType<RepositoryTokenStore<MyToken>>(provider.GetRequiredService<ITokenStore<MyToken>>());
        Assert.Null(provider.GetService<ITokenStore<SchemataToken>>());
    }

    [Fact]
    public void Forward_The_Default_UseSecurity_To_The_Built_In_Entities() {
        var builder = new SchemataBuilder(new ConfigurationBuilder().Build(), null!);
        builder.UseSecurity();

        Assert.True(builder.HasFeature<SchemataSecurityFeature<SchemataSecurity, SchemataToken>>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Publish_Redeem_Rotate_And_Revoke_Host_Token_Rows(bool linq) {
        await using var host = await Host.CreateAsync(linq);

        var (store, scope) = host.CreateStoreScope();
        using (scope) {
            await store.CreateAsync(new MyToken {
                Name        = "access",
                ReferenceId = "access",
                Status      = Statuses.Valid,
                Application = "applications/client",
            });

            var published = await store.FindByReferenceIdAsync("access");
            Assert.IsType<MyToken>(published);
            Assert.Equal(Statuses.Valid, published!.Status);

            Assert.True(await store.TryRedeemAsync(published));
        }

        using (var verifyScope = host.Services.CreateScope()) {
            var redeemer = verifyScope.ServiceProvider.GetRequiredService<RepositoryTokenStore<MyToken>>();
            Assert.Equal(Statuses.Redeemed, (await redeemer.FindByReferenceIdAsync("access"))!.Status);

            await redeemer.CreateAsync(new MyToken {
                Name        = "predecessor",
                ReferenceId = "predecessor",
                Status      = Statuses.Valid,
                Application = "applications/client",
            });

            var predecessor = (await redeemer.FindByReferenceIdAsync("predecessor"))!;
            var successor = new MyToken {
                Name        = "successor",
                ReferenceId = "successor",
                Status      = Statuses.Valid,
                Application = "applications/client",
            };

            Assert.True(await redeemer.TryRotateAsync(predecessor, [successor]));

            // Failure paths: a redeemed predecessor cannot rotate, and a redeemed row cannot redeem.
            Assert.False(await redeemer.TryRotateAsync(predecessor, [new MyToken {
                Name = "late", ReferenceId = "late", Status = Statuses.Valid,
            }]));
            Assert.False(await redeemer.TryRedeemAsync(predecessor));
        }

        using var revokeScope = host.Services.CreateScope();
        var revoker = revokeScope.ServiceProvider.GetRequiredService<RepositoryTokenStore<MyToken>>();
        var rotated = (await revoker.FindByReferenceIdAsync("successor"))!;
        Assert.IsType<MyToken>(rotated);
        Assert.Equal(Statuses.Valid, rotated.Status);

        await revoker.RevokeAsync(rotated);

        using var final = host.Services.CreateScope();
        var persisted = (await final.ServiceProvider.GetRequiredService<RepositoryTokenStore<MyToken>>()
                                   .FindByReferenceIdAsync("successor"))!;
        Assert.IsType<MyToken>(persisted);
        Assert.Equal(Statuses.Revoked, persisted.Status);
    }

    private sealed class Host(ServiceProvider services, string path) : IAsyncDisposable
    {
        public ServiceProvider Services => services;

        public static async Task<Host> CreateAsync(bool linq) {
            var path = Path.Combine(Path.GetTempPath(), $"schemata-custom-token-{Guid.NewGuid():N}.db");
            var services = new ServiceCollection();
            if (linq) {
                var schema = new MappingSchema();
                schema.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());
                var options = new DataOptions().UseSQLite($"Data Source={path};Pooling=False").UseMappingSchema(schema);
                services.AddSingleton<Func<CustomConnection>>(_ => () => new(options));
                using var db = new CustomConnection(options);
                db.CreateTable<MyToken>("MyTokens");
                db.CreateTable<MySecurity>("MySecurities");
                services.AddRepository<MyToken, LinqToDbRepository<CustomConnection, MyToken>>();
            } else {
                services.AddDbContextFactory<CustomContext>(o => o.UseSqlite($"Data Source={path};Pooling=False")
                    .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
                services.AddRepository<MyToken, EfCoreRepository<CustomContext, MyToken>>();
            }

            services.AddSingleton(TimeProvider.System);
            var builder = new SchemataBuilder(new ConfigurationBuilder().Build(), null!);
            builder.UseSecurity<MySecurity, MyToken>();
            new SchemataSecurityFeature<MySecurity, MyToken>()
                .ConfigureServices(services, builder.Options, builder.Configurators, builder.Configuration, null!);
            var root = services.BuildServiceProvider();
            if (!linq) {
                await using var db = await root.GetRequiredService<IDbContextFactory<CustomContext>>().CreateDbContextAsync();
                await db.Database.EnsureCreatedAsync();
            }

            return new(root, path);
        }

        public (RepositoryTokenStore<MyToken> Store, IServiceScope Scope) CreateStoreScope() {
            var scope = Services.CreateScope();
            return (scope.ServiceProvider.GetRequiredService<RepositoryTokenStore<MyToken>>(), scope);
        }

        public async ValueTask DisposeAsync() {
            await services.DisposeAsync();
            File.Delete(path);
        }
    }
}
