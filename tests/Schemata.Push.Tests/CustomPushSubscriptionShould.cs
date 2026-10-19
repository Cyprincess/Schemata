using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Core;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.LinqToDB;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Messaging.Skeleton;
using Schemata.Push.Foundation.Commands;
using Schemata.Push.Foundation.Features;
using Schemata.Push.Foundation.Handlers;
using Schemata.Push.Skeleton;
using Schemata.Push.Skeleton.Entities;
using Xunit;
using IndexAttribute = Schemata.Abstractions.Entities.IndexAttribute;
using TableAttribute = System.ComponentModel.DataAnnotations.Schema.TableAttribute;

namespace Schemata.Push.Tests;

/// <summary>
///     Proves <c>UsePush&lt;TPushSubscription&gt;</c> closes the feature, repositories, and handlers
///     on the host-selected subscription entity, and that the selected entity round-trips through
///     the dispatcher and subscription manager on EF Core and LinqToDB.
/// </summary>
[Trait("Layer", "Integration")]
public sealed class CustomPushSubscriptionShould
{
    [Fact]
    public void Forward_The_Default_UsePush_To_The_Built_In_Entity() {
        var builder = new SchemataBuilder(new ConfigurationBuilder().Build(), null!);
        builder.UsePush();

        Assert.True(builder.HasFeature<SchemataPushFeature<SchemataPushSubscription>>());
    }

    [Fact]
    public void Close_The_Handlers_On_The_Selected_Entity() {
        var builder = new SchemataBuilder(new ConfigurationBuilder().Build(), null!);
        builder.UsePush<CustomPushSubscription>();

        var services = new ServiceCollection();
        services.AddSingleton(new Mock<IRepository<CustomPushSubscription>>().Object);
        services.AddSingleton(new Mock<IResourceMutation<CustomPushSubscription>>().Object);
        builder.Invoke(services);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.True(builder.HasFeature<SchemataPushFeature<CustomPushSubscription>>());
        Assert.IsType<AddPushSubscriptionHandler<CustomPushSubscription>>(
            scope.ServiceProvider.GetRequiredService<IRequestHandler<AddPushSubscriptionRequest, SchemataPushSubscription>>());
        Assert.IsType<RemovePushSubscriptionHandler<CustomPushSubscription>>(
            scope.ServiceProvider.GetRequiredService<IRequestHandler<RemovePushSubscriptionRequest, Unit>>());
        Assert.IsType<GetPushSubscriptionsHandler<CustomPushSubscription>>(
            scope.ServiceProvider.GetRequiredService<IRequestHandler<GetPushSubscriptionsQuery, IReadOnlyList<SchemataPushSubscription>>>());
        Assert.IsType<ExistsPushSubscriptionHandler<CustomPushSubscription>>(
            scope.ServiceProvider.GetRequiredService<IRequestHandler<ExistsPushSubscriptionQuery, bool>>());
        Assert.Null(provider.GetService<IRepository<SchemataPushSubscription>>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Add_Get_Exists_And_Remove_The_Custom_Subscription(bool linq) {
        await using var host = await Host.CreateAsync(linq);

        using (var scope = host.Services.CreateScope()) {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
            var created = await dispatcher.SendAsync<AddPushSubscriptionRequest, SchemataPushSubscription>(
                new("owners/one", "email", "primary", new Dictionary<string, string?> { ["locale"] = "en" }));

            var custom = Assert.IsType<CustomPushSubscription>(created);
            Assert.Equal("customPushSubscriptions/custom-1", custom.CanonicalName);
            Assert.Equal("en", custom.Metadata!["locale"]);

            var repeated = await dispatcher.SendAsync<AddPushSubscriptionRequest, SchemataPushSubscription>(
                new("owners/one", "email", "primary"));
            Assert.Equal(custom.Uid, repeated.Uid);

            var rows = await dispatcher.SendAsync<GetPushSubscriptionsQuery, IReadOnlyList<SchemataPushSubscription>>(
                new("owners/one"));
            Assert.IsType<CustomPushSubscription>(Assert.Single(rows));

            Assert.True(await dispatcher.SendAsync<ExistsPushSubscriptionQuery, bool>(new("owners/one", "email", "primary")));
            Assert.False(await dispatcher.SendAsync<ExistsPushSubscriptionQuery, bool>(new("owners/one", "email", "other")));
        }

        using (var scope = host.Services.CreateScope()) {
            var manager = scope.ServiceProvider.GetRequiredService<IPushSubscriptionManager>();
            var added = await manager.AddAsync("owners/two", "sms", "secondary");
            Assert.IsType<CustomPushSubscription>(added);
            Assert.Equal(1, await CountAsync(manager.GetForOwnerAsync("owners/two")));
        }

        using (var scope = host.Services.CreateScope()) {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
            await dispatcher.SendAsync<RemovePushSubscriptionRequest, Unit>(new("owners/one", "email", "primary"));
        }

        using (var scope = host.Services.CreateScope()) {
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<CustomPushSubscription>>();
            {
                var retained = await repository.FirstOrDefaultAsync(
                    query => query.Where(row => row.Owner == "owners/one" && row.ProviderKey == "primary"),
                    default,
                    new QueryOptions(IgnoreSoftDelete: true));
                Assert.IsType<CustomPushSubscription>(retained);
                Assert.NotNull(retained!.DeleteTime);
            }
        }

        Assert.Null(host.Services.GetService<IRepository<SchemataPushSubscription>>());
    }

    private static async Task<int> CountAsync(IAsyncEnumerable<SchemataPushSubscription> subscriptions) {
        var count = 0;
        await foreach (var _ in subscriptions) {
            count++;
        }

        return count;
    }

    [Table("CustomPushSubscriptions")]
    [CanonicalName("customPushSubscriptions/{custom_push_subscription}")]
    [Schemata.Abstractions.Entities.PrimaryKey(nameof(Uid))]
    [Index(nameof(Owner), nameof(Provider), nameof(ProviderKey), IsUnique = true)]
    [IndexAttribute(nameof(Owner), nameof(Provider), nameof(ProviderKey), IsUnique = true)]
    public sealed class CustomPushSubscription : SchemataPushSubscription;

    public sealed class Database(DbContextOptions<Database> options) : DbContext(options)
    {
        public DbSet<CustomPushSubscription> Subscriptions { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder model) {
            // The CLR base type is concrete; map the selected type as its own root so the unselected
            // built-in subscription does not join the model.
            model.Entity<CustomPushSubscription>().HasBaseType((Type?)null);
        }
    }

    public sealed class Connection(DataOptions options) : DataConnection(options);

    private sealed class CustomNameAdvisor(Func<string> nextName) : IRepositoryAddAdvisor<CustomPushSubscription>
    {
        public int Order => AdviceAddCanonicalName.DefaultOrder - 1_000_000;

        public Task<AdviseResult> AdviseAsync(
            AdviceContext                 ctx,
            IRepository<CustomPushSubscription> repository,
            CustomPushSubscription        entity,
            MutationOptions      options,
            CancellationToken             ct
        ) {
            entity.Name ??= nextName();
            return Task.FromResult(AdviseResult.Continue);
        }
    }

    private sealed class Host(ServiceProvider services, string path) : IAsyncDisposable
    {
        public ServiceProvider Services => services;

        public static async Task<Host> CreateAsync(bool linq) {
            var path     = Path.Combine(Path.GetTempPath(), $"schemata-custom-push-{Guid.NewGuid():N}.db");
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<IRepositoryAddAdvisor<CustomPushSubscription>>(
                new CustomNameAdvisor(new NameSequence().Next));

            if (linq) {
                var mapping = new MappingSchema();
                mapping.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());
                var options = new DataOptions().UseSQLite($"Data Source={path};Pooling=False").UseMappingSchema(mapping);
                services.AddSingleton<Func<Connection>>(_ => () => new(options));
                services.AddRepository<CustomPushSubscription, LinqToDbRepository<Connection, CustomPushSubscription>>();
                services.AddScoped<IUnitOfWork<Connection>, LinqToDbUnitOfWork<Connection>>();

                var builder = new SchemataBuilder(new ConfigurationBuilder().Build(), null!);
                builder.UsePush<CustomPushSubscription>();
                builder.Invoke(services);

                using var connection = new Connection(options);
                connection.CreateTable<CustomPushSubscription>(tableOptions: TableOptions.CreateIfNotExists);
                connection.Execute(
                    "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_CustomPushSubscriptions_Owner_Provider_ProviderKey\" "
                  + "ON \"CustomPushSubscriptions\" (\"Owner\", \"Provider\", \"ProviderKey\")");
            } else {
                services.AddDbContextFactory<Database>(options => options
                    .UseSqlite($"Data Source={path};Pooling=False")
                    .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
                services.AddRepository<CustomPushSubscription, EfCoreRepository<Database, CustomPushSubscription>>();
                services.AddScoped<IUnitOfWork<Database>, EfCoreUnitOfWork<Database>>();

                var builder = new SchemataBuilder(new ConfigurationBuilder().Build(), null!);
                builder.UsePush<CustomPushSubscription>();
                builder.Invoke(services);
            }

            var root = services.BuildServiceProvider();
            if (!linq) {
                await using var database = await root.GetRequiredService<IDbContextFactory<Database>>().CreateDbContextAsync();
                await database.Database.EnsureCreatedAsync();
            }

            return new(root, path);
        }

        public async ValueTask DisposeAsync() {
            await services.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }
    }

    private sealed class NameSequence
    {
        private int _names;

        public string Next() => $"custom-{++_names}";
    }
}
