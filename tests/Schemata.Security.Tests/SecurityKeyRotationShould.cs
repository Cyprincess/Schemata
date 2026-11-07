using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Scheduling.Skeleton;
using Schemata.Security.Foundation;
using Schemata.Security.Foundation.Services;
using Schemata.Security.Foundation.Stores;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Schemata.Security.Tests.Fixtures;
using Xunit;

namespace Schemata.Security.Tests;

[Trait("Category", "Integration")]
public sealed class SecurityKeyRotationShould
{
    private const string Parent = "https://as.example";

    private static readonly DateTimeOffset Now = new(2035, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Rotate_Publishes_Staged_Key_Before_Activating_It_And_Retiring_The_Previous_Key() {
        await using var host = await RotationHost.CreateAsync(new());
        await using (var scope = host.Services.CreateAsyncScope()) {
            await SeedActiveAsync(scope.ServiceProvider, SecurityConstants.Usages.Signing);
        }

        SchemataSecurity rotated;
        Guid previousUid;
        Guid previousTimestamp;
        await using (var scope = host.Services.CreateAsyncScope()) {
            var store = scope.ServiceProvider.GetRequiredService<ISecurityStore<SchemataSecurity>>();
            var previous = await ActiveAsync(store, SecurityConstants.Usages.Signing);
            previousUid = previous.Uid;
            previousTimestamp = previous.Timestamp;

            rotated = await scope.ServiceProvider.GetRequiredService<SecurityKeyRotationService<SchemataSecurity>>()
                .RotateAsync(Parent, SecurityConstants.Usages.Signing, SecurityConstants.Algorithms.Rsa, default);
        }

        Assert.Equal(SecurityConstants.Statuses.Valid, rotated.Status);
        Assert.Equal(Now.UtcDateTime, rotated.ActivationTime);
        Assert.Null(rotated.RetirementTime);
        Assert.False(string.IsNullOrWhiteSpace(rotated.Name));
        Assert.Equal($"securities/{rotated.Name}", rotated.CanonicalName);
        using (var rsa = RSA.Create()) {
            rsa.ImportFromPem(rotated.Value);
            Assert.Equal(2048, rsa.KeySize);
        }

        await using var verify = host.Services.CreateAsyncScope();
        var rows = await ListAsync(verify.ServiceProvider.GetRequiredService<ISecurityStore<SchemataSecurity>>(),
            SecurityConstants.Usages.Signing);
        Assert.Equal(2, rows.Count);
        Assert.Single(rows, row => row.Status == SecurityConstants.Statuses.Valid);
        var retired = Assert.Single(rows, row => row.Status == SecurityConstants.Statuses.Retired);
        Assert.Equal(previousUid, retired.Uid);
        Assert.Equal(Now.UtcDateTime, retired.RetirementTime);
        Assert.NotEqual(previousTimestamp, retired.Timestamp);
    }

    [Fact]
    public async Task Concurrent_Rotation_Commits_One_Transition_And_Keeps_The_Previous_Key_Retained() {
        var generator = new BarrierGenerator();
        await using var host = await RotationHost.CreateAsync(new(), services => {
            services.Replace(ServiceDescriptor.Singleton<ISecurityKeyGenerator<SchemataSecurity>>(generator));
        });
        await using (var scope = host.Services.CreateAsyncScope()) {
            await SeedActiveAsync(scope.ServiceProvider, SecurityConstants.Usages.Signing);
        }

        var previousUid = (await ActiveAsync(host.Services, SecurityConstants.Usages.Signing)).Uid;
        await using var firstScope = host.Services.CreateAsyncScope();
        await using var secondScope = host.Services.CreateAsyncScope();
        var first = firstScope.ServiceProvider.GetRequiredService<SecurityKeyRotationService<SchemataSecurity>>()
            .RotateAsync(Parent, SecurityConstants.Usages.Signing, SecurityConstants.Algorithms.Rsa, default);
        var second = secondScope.ServiceProvider.GetRequiredService<SecurityKeyRotationService<SchemataSecurity>>()
            .RotateAsync(Parent, SecurityConstants.Usages.Signing, SecurityConstants.Algorithms.Rsa, default);

        var failures = await Task.WhenAll(CaptureFailure(first), CaptureFailure(second));

        Assert.Equal(1, failures.Count(failure => failure is null));
        Assert.NotNull(failures.Single(failure => failure is not null));

        await using var verify = host.Services.CreateAsyncScope();
        var rows = await ListAsync(verify.ServiceProvider.GetRequiredService<ISecurityStore<SchemataSecurity>>(),
            SecurityConstants.Usages.Signing);
        Assert.Single(rows, row => row.Status == SecurityConstants.Statuses.Valid);
        Assert.Single(rows, row => row.Status == SecurityConstants.Statuses.Staged);
        var previous = Assert.Single(rows, row => row.Uid == previousUid);
        Assert.Equal(SecurityConstants.Statuses.Retired, previous.Status);
        Assert.Equal(Now.UtcDateTime, previous.RetirementTime);
    }

    [Fact]
    public async Task Failed_Activation_Rolls_Back_And_Leaves_The_Previous_Key_Active() {
        await using var host = await RotationHost.CreateAsync(new(), services => {
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryUpdateAdvisor<SchemataSecurity>, BlockRetirement>());
        });
        await using (var scope = host.Services.CreateAsyncScope()) {
            await SeedActiveAsync(scope.ServiceProvider, SecurityConstants.Usages.Signing);
        }
        var previousUid = (await ActiveAsync(host.Services, SecurityConstants.Usages.Signing)).Uid;

        await using (var scope = host.Services.CreateAsyncScope()) {
            var rotation = scope.ServiceProvider.GetRequiredService<SecurityKeyRotationService<SchemataSecurity>>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => rotation.RotateAsync(
                Parent, SecurityConstants.Usages.Signing, SecurityConstants.Algorithms.Rsa, default));
        }

        await using var verify = host.Services.CreateAsyncScope();
        var rows = await ListAsync(verify.ServiceProvider.GetRequiredService<ISecurityStore<SchemataSecurity>>(),
            SecurityConstants.Usages.Signing);
        Assert.Single(rows, row => row.Status == SecurityConstants.Statuses.Staged);
        var previous = Assert.Single(rows, row => row.Uid == previousUid);
        Assert.Equal(SecurityConstants.Statuses.Valid, previous.Status);
        Assert.Single(rows, row => row.Status == SecurityConstants.Statuses.Valid);
    }

    [Fact]
    public async Task External_Generator_Supplies_Material_For_The_Requested_Key_Slot() {
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportPkcs8PrivateKeyPem();
        SecurityKeyGenerationContext? received = null;
        var generator = new Mock<ISecurityKeyGenerator<SchemataSecurity>>();
        generator.Setup(value => value.GenerateAsync(It.IsAny<SecurityKeyGenerationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SecurityKeyGenerationContext context, CancellationToken _) => {
                received = context;
                return new() {
                    Kind = SecurityConstants.Kinds.PrivateKey,
                    Kid = "external-key",
                    Value = pem,
                };
            });
        await using var host = await RotationHost.CreateAsync(new(), services => {
            services.Replace(ServiceDescriptor.Scoped<ISecurityKeyGenerator<SchemataSecurity>>(_ => generator.Object));
        });

        await using var scope = host.Services.CreateAsyncScope();
        var rotated = await scope.ServiceProvider.GetRequiredService<SecurityKeyRotationService<SchemataSecurity>>()
            .RotateAsync(Parent, SecurityConstants.Usages.Encryption, SecurityConstants.Algorithms.Rsa, default);

        Assert.NotNull(received);
        Assert.Equal(Parent, received.Parent);
        Assert.Equal(SecurityConstants.Usages.Encryption, received.Usage);
        Assert.Equal(SecurityConstants.Algorithms.Rsa, received.Algorithm);
        Assert.Equal("external-key", rotated.Kid);
        Assert.Equal(pem, rotated.Value);
        Assert.Equal(SecurityConstants.Statuses.Valid, rotated.Status);
    }

    [Fact]
    public async Task Scheduled_Job_Rotates_Configured_Signing_And_Encryption_Slots_Through_The_Rotation_Service() {
        var options = new SchemataSecurityOptions { RotationInterval = TimeSpan.FromHours(1) };
        options.Rotations.Add(new() {
            Parent = Parent,
            Usage = SecurityConstants.Usages.Signing,
            Algorithm = SecurityConstants.Algorithms.Rsa,
        });
        options.Rotations.Add(new() {
            Parent = Parent,
            Usage = SecurityConstants.Usages.Encryption,
            Algorithm = SecurityConstants.Algorithms.Rsa,
        });
        await using var host = await RotationHost.CreateAsync(options);

        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SecurityKeyRotationJob<SchemataSecurity>>()
            .ExecuteAsync(new(), default);

        var store = scope.ServiceProvider.GetRequiredService<ISecurityStore<SchemataSecurity>>();
        var signing = await ActiveAsync(store, SecurityConstants.Usages.Signing);
        var encryption = await ActiveAsync(store, SecurityConstants.Usages.Encryption);
        Assert.Equal(Now.UtcDateTime, signing.ActivationTime);
        Assert.Equal(Now.UtcDateTime, encryption.ActivationTime);
        Assert.NotEqual(signing.Kid, encryption.Kid);
    }

    private static async Task<SchemataSecurity> SeedActiveAsync(IServiceProvider services, string usage) {
        using var rsa = RSA.Create(2048);
        var store = services.GetRequiredService<ISecurityStore<SchemataSecurity>>();
        return await store.CreateAsync(new() {
            Parent = Parent,
            Kind = SecurityConstants.Kinds.PrivateKey,
            Usage = usage,
            Algorithm = SecurityConstants.Algorithms.Rsa,
            Kid = $"previous-{Guid.NewGuid():n}",
            Value = rsa.ExportPkcs8PrivateKeyPem(),
            Status = SecurityConstants.Statuses.Valid,
            ActivationTime = Now.AddDays(-1).UtcDateTime,
        }) ?? throw new InvalidOperationException("The active fixture was not created.");
    }

    private static async Task<SchemataSecurity> ActiveAsync(IServiceProvider services, string usage) {
        return await ActiveAsync(services.GetRequiredService<ISecurityStore<SchemataSecurity>>(), usage);
    }

    private static async Task<SchemataSecurity> ActiveAsync(ISecurityStore<SchemataSecurity> store, string usage) {
        await foreach (var row in store.ListByParentAsync(Parent, usage: usage, status: SecurityConstants.Statuses.Valid)) {
            return row;
        }

        throw new InvalidOperationException("The active fixture is missing.");
    }

    private static async Task<List<SchemataSecurity>> ListAsync(ISecurityStore<SchemataSecurity> store, string usage) {
        var rows = new List<SchemataSecurity>();
        await foreach (var row in store.ListByParentAsync(Parent, usage: usage)) {
            rows.Add(row);
        }

        return rows;
    }

    private static async Task<Exception?> CaptureFailure(Task task) {
        try {
            await task;
            return null;
        } catch (Exception exception) {
            return exception;
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class BarrierGenerator : ISecurityKeyGenerator<SchemataSecurity>
    {
        private readonly TaskCompletionSource _both = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public async Task<SchemataSecurity> GenerateAsync(SecurityKeyGenerationContext context, CancellationToken ct) {
            using var rsa = RSA.Create(2048);
            var row = new SchemataSecurity {
                Kind = SecurityConstants.Kinds.PrivateKey,
                Kid = $"generated-{Guid.NewGuid():n}",
                Value = rsa.ExportPkcs8PrivateKeyPem(),
            };
            if (Interlocked.Increment(ref _arrivals) == 2) {
                _both.SetResult();
            }

            await _both.Task.WaitAsync(ct);
            return row;
        }
    }

    private sealed class BlockRetirement : IRepositoryUpdateAdvisor<SchemataSecurity>
    {
        public int Order => 0;

        public Task<AdviseResult> AdviseAsync(
            AdviceContext context,
            IRepository<SchemataSecurity> repository,
            SchemataSecurity entity,
            MutationOptions options,
            CancellationToken ct
        ) {
            return Task.FromResult(entity.Status == SecurityConstants.Statuses.Retired
                ? AdviseResult.Block
                : AdviseResult.Continue);
        }
    }

    private sealed class SecurityNameAdvisor : IRepositoryAddAdvisor<SchemataSecurity>
    {
        public int Order => AdviceAddCanonicalName.DefaultOrder - 1;

        public Task<AdviseResult> AdviseAsync(
            AdviceContext context,
            IRepository<SchemataSecurity> repository,
            SchemataSecurity entity,
            MutationOptions options,
            CancellationToken ct
        ) {
            entity.Name ??= $"security-{Guid.NewGuid():n}";
            return Task.FromResult(AdviseResult.Continue);
        }
    }

    private sealed class Context(DbContextOptions<Context> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model) {
            model.Entity<SchemataSecurity>();
        }
    }

    private sealed class RotationHost : IAsyncDisposable
    {
        private readonly string _path;

        private RotationHost(ServiceProvider services, string path) {
            Services = services;
            _path = path;
        }

        public ServiceProvider Services { get; }

        public static async Task<RotationHost> CreateAsync(
            SchemataSecurityOptions options,
            Action<IServiceCollection>? configure = null
        ) {
            var path = Path.Combine(Path.GetTempPath(), $"schemata-key-rotation-{Guid.NewGuid():n}.db");
            var services = new ServiceCollection();
            services.AddDbContextFactory<Context>(db => db.UseSqlite($"Data Source={path};Pooling=False")
                .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
            services.AddRepository<SchemataSecurity, EfCoreRepository<Context, SchemataSecurity>>();
            SecurityTestResources.AddResourceTypeResolver(services);
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataSecurity>, SecurityNameAdvisor>());
            services.AddSingleton(Options.Create(options));
            services.AddSingleton<TimeProvider>(new FixedTimeProvider());
            services.TryAddScoped<ISecurityStore<SchemataSecurity>, SecurityStore<SchemataSecurity>>();
            services.TryAddScoped<ISecurityKeyGenerator<SchemataSecurity>, RsaSecurityKeyGenerator<SchemataSecurity>>();
            services.TryAddScoped<SecurityKeyRotationService<SchemataSecurity>>();
            services.TryAddTransient<SecurityKeyRotationJob<SchemataSecurity>>();
            configure?.Invoke(services);

            var root = services.BuildServiceProvider();
            await using var context = await root.GetRequiredService<IDbContextFactory<Context>>().CreateDbContextAsync();
            await context.Database.EnsureCreatedAsync();
            return new(root, path);
        }

        public async ValueTask DisposeAsync() {
            await Services.DisposeAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(_path)) {
                File.Delete(_path);
            }
        }
    }
}
