using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Common;
using Schemata.Report.Foundation;
using Schemata.Report.Foundation.Handlers;
using Schemata.Report.Foundation.Queries;
using Schemata.Report.Skeleton;
using Schemata.Report.Skeleton.Entities;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Xunit;

using Schemata.Abstractions.Advisors;
namespace Schemata.Report.Tests;

[Trait("Category", "Integration")]
public sealed class ReportContinuationShould
{
    private const string Snapshot = "reports/A/snapshots/daily";
    private static readonly Guid SnapshotUid = Guid.Parse("99999999-9999-9999-9999-999999999999");

    [Theory]
    [Trait("Layer", "Integration")]
    [InlineData("snapshot")]
    [InlineData("uid")]
    [InlineData("missing-uid")]
    [InlineData("page-size")]
    [InlineData("subject")]
    [InlineData("tenant")]
    [InlineData("tamper")]
    [InlineData("truncate")]
    [InlineData("key-ring")]
    [InlineData("negative-chunk")]
    [InlineData("negative-offset")]
    [InlineData("unprotected")]
    public async Task Reject_Changed_Bindings_And_Invalid_Positions_Before_Chunk_IO(string change) {
        await using var scopes = await ScopesAsync();
        await using var restarted = await ScopesAsync();
        var protection = scopes.Issuing.GetRequiredService<IDataProtectionProvider>();
        var protector = protection.CreateProtector(ReportReadPageToken.ProtectionPurpose);
        var subject = Principal("alice");
        string token;
        var binding = ReportReadPageToken.Bind(Snapshot, SnapshotUid, 1, subject);
        token = change switch {
            "uid" => ProtectedContinuation.Encode(protector, binding with { Uid = Guid.NewGuid() }),
            "missing-uid" => ProtectedContinuation.Encode(protector, new {
                Snapshot, PageSize = 1,
                Caller = ProtectedContinuationCaller.Bind(subject), ChunkIndex = 0, Offset = 1,
            }),
            "negative-chunk" => ProtectedContinuation.Encode(protector, binding with { ChunkIndex = -1 }),
            "negative-offset" => ProtectedContinuation.Encode(protector, binding with { Offset = -1 }),
            "unprotected" => Convert.ToBase64String(new byte[8]),
            _ => binding.Encode(protector, 0, 1),
        };
        if (change == "tamper") token = (token[0] == 'A' ? "B" : "A") + token[1..];
        if (change == "truncate") token = token[..^8];
        var store = new Mock<IReportSnapshotStore>(MockBehavior.Strict);
        store.Setup(value => value.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new SchemataReportSnapshot { Uid = SnapshotUid, ChunkCount = 1 });
        var handler = new ReadSnapshotHandler<SchemataReportSnapshot>(store.Object,
            Options.Create(new SchemataReportOptions()),
            change switch {
                "tenant" => scopes.Sibling.GetRequiredService<IDataProtectionProvider>(),
                "key-ring" => restarted.Issuing.GetRequiredService<IDataProtectionProvider>(),
                _ => protection,
            });
        var error = await Assert.ThrowsAsync<InvalidArgumentException>(() => handler.HandleAsync(new() {
            CanonicalName = change == "snapshot" ? "reports/B/snapshots/daily" : Snapshot,
            PageSize = change == "page-size" ? 2 : 1,
            Principal = change == "subject" ? Principal("bob") : subject,
            PageToken = token,
        }, Advice()));
        Assert.Equal(SchemataResources.INVALID_PAGE_TOKEN, Assert.Single(error.Details!.OfType<ErrorInfoDetail>()).Reason);
        store.Verify(value => value.GetChunkAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Integration")]
    public async Task Continue_After_Key_Ring_Restart_With_Exact_Rows_And_Normalized_Page_Size(bool tenancy) {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "report-continuation-" + Guid.NewGuid().ToString("N")));
        var tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
        try {
            var store = Store();
            string? token;
            var rows = new List<int>();
            await using (var firstHost = await HostAsync(directory, tenancy ? tenant : null)) {
                var handler = Handler(store.Object, firstHost.Provider.GetRequiredService<IDataProtectionProvider>());
                var principal = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, "alice"), new Claim("sub", "shadow"),
                ], "test"));
                var first = await handler.HandleAsync(new() { CanonicalName = Snapshot, PageSize = 100, Principal = principal }, Advice());
                rows.AddRange(first.Rows.Select(Value));
                token = first.NextPageToken;
                Assert.NotNull(token);
            }
            await using (var restartedHost = await HostAsync(directory, tenancy ? tenant : null)) {
                var handler = Handler(store.Object, restartedHost.Provider.GetRequiredService<IDataProtectionProvider>());
                do {
                    var page = await handler.HandleAsync(new() { Name = Snapshot, PageSize = 2, PageToken = token, Principal = Principal("alice", "sub") }, Advice());
                    rows.AddRange(page.Rows.Select(Value));
                    token = page.NextPageToken;
                } while (token is not null);
            }
            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, rows);
        } finally {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    [Trait("Layer", "Unit")]
    public async Task Preserve_Repository_Failures_Outside_Token_Decoding() {
        var failure = new IOException("snapshot-storage-failure");
        var store = new Mock<IReportSnapshotStore>();
        store.Setup(value => value.GetAsync(Snapshot, It.IsAny<CancellationToken>())).ThrowsAsync(failure);
        var handler = Handler(store.Object, new EphemeralDataProtectionProvider());
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => handler.HandleAsync(new() { Name = Snapshot }, Advice())));
    }

    [Fact]
    [Trait("Layer", "Integration")]
    public async Task Reject_Chunk_Overflow_With_Canonical_Argument_Error() {
        var protection = new EphemeralDataProtectionProvider();
        var store = new Mock<IReportSnapshotStore>();
        store.Setup(value => value.GetAsync(Snapshot, It.IsAny<CancellationToken>())).ReturnsAsync(new SchemataReportSnapshot());
        store.Setup(value => value.GetChunkAsync(Snapshot, int.MaxValue, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchemataReportSnapshotChunk { Rows = "[{\"value\":1}]" });
        var token = ReportReadPageToken.Bind(Snapshot, Guid.Empty, 1, null)
            .Encode(protection.CreateProtector(ReportReadPageToken.ProtectionPurpose), int.MaxValue, 0);
        var handler = Handler(store.Object, protection);
        var error = await Assert.ThrowsAsync<InvalidArgumentException>(() => handler.HandleAsync(new() { Name = Snapshot, PageSize = 1, PageToken = token }, Advice()));
        Assert.Equal(SchemataResources.INVALID_PAGE_TOKEN, Assert.Single(error.Details!.OfType<ErrorInfoDetail>()).Reason);
    }

    [Fact]
    [Trait("Layer", "Integration")]
    public async Task Reject_Old_Continuation_After_Delete_Recreate_Under_Same_Name() {
        var protection = new EphemeralDataProtectionProvider();
        var protector = protection.CreateProtector(ReportReadPageToken.ProtectionPurpose);
        var recreated = new SchemataReportSnapshot { Uid = Guid.NewGuid(), ChunkCount = 1 };
        var stale = ReportReadPageToken.Bind(Snapshot, Guid.NewGuid(), 1, Principal("alice")).Encode(protector, 0, 0);

        var store = new Mock<IReportSnapshotStore>(MockBehavior.Strict);
        store.Setup(value => value.GetAsync(Snapshot, It.IsAny<CancellationToken>())).ReturnsAsync(recreated);
        var handler = Handler(store.Object, protection);
        var error = await Assert.ThrowsAsync<InvalidArgumentException>(() => handler.HandleAsync(new() {
            Name = Snapshot, PageSize = 1, Principal = Principal("alice"), PageToken = stale,
        }, Advice()));
        Assert.Equal(SchemataResources.INVALID_PAGE_TOKEN, Assert.Single(error.Details!.OfType<ErrorInfoDetail>()).Reason);
        store.Verify(value => value.GetChunkAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    [Trait("Layer", "Integration")]
    public async Task Continue_With_Fresh_Tokens_After_Delete_Recreate_Under_Same_Name() {
        var protection = new EphemeralDataProtectionProvider();
        var recreated = new SchemataReportSnapshot { Uid = Guid.NewGuid(), ChunkCount = 2 };
        var store = new Mock<IReportSnapshotStore>(MockBehavior.Strict);
        store.Setup(value => value.GetAsync(Snapshot, It.IsAny<CancellationToken>())).ReturnsAsync(recreated);
        store.Setup(value => value.GetChunkAsync(Snapshot, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchemataReportSnapshotChunk { Rows = "[{\"value\":1},{\"value\":2}]" });
        store.Setup(value => value.GetChunkAsync(Snapshot, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchemataReportSnapshotChunk { Rows = "[{\"value\":3},{\"value\":4}]" });
        var handler = Handler(store.Object, protection);

        var first = await handler.HandleAsync(new() { Name = Snapshot, PageSize = 2, Principal = Principal("alice") }, Advice());
        Assert.NotNull(first.NextPageToken);
        var second = await handler.HandleAsync(new() {
            Name = Snapshot, PageSize = 2, Principal = Principal("alice"), PageToken = first.NextPageToken,
        }, Advice());

        Assert.Null(second.NextPageToken);
        Assert.Equal([1, 2, 3, 4], first.Rows.Select(Value).Concat(second.Rows.Select(Value)));
    }

    private static ClaimsPrincipal Principal(string subject, string claim = ClaimTypes.NameIdentifier) =>
        new(new ClaimsIdentity([new Claim(claim, subject)], "test"));

    private static ReadSnapshotHandler<SchemataReportSnapshot> Handler(IReportSnapshotStore store, IDataProtectionProvider protection) =>
        new(store, Options.Create(new SchemataReportOptions { MaxReadPageSize = 2 }), protection);

    private static async Task<ContinuationScopes> ScopesAsync() {
        var services = new ServiceCollection()
                      .AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider())
                      .AddSingleton(Manager());
        new SchemataTenancyBuilder<SchemataTenant>(services).UseState(state => state.CookiePrefix = "r.");
        var root = services.BuildServiceProvider();
        var tenancy = root.GetRequiredService<IOptions<SchemataTenancyOptions>>();
        var cache = new MemoryCacheTenantProviderCache(tenancy);
        var scopes = new SchemataTenantServiceScopeFactory<SchemataTenant>(
            root, new SchemataTenantServiceProviderFactory<SchemataTenant>(root, cache, tenancy));
        return new(root, cache,
            await scopes.CreateAsync(new(Guid.Parse(ContinuationScopes.IssuingTenant))),
            await scopes.CreateAsync(new(Guid.Parse(ContinuationScopes.SiblingTenant))));
    }

    private static async Task<ContinuationHost> HostAsync(DirectoryInfo directory, Guid? tenant) {
        var services = new ServiceCollection();
        services.AddDataProtection().PersistKeysToFileSystem(directory).SetApplicationName("ReportContinuationTests");
        if (tenant is not null) {
            services.AddSingleton(Manager());
            new SchemataTenancyBuilder<SchemataTenant>(services).UseState(state => state.CookiePrefix = "r.");
        }
        var root = services.BuildServiceProvider();
        if (tenant is null) return new(root, null, null);
        var tenancy = root.GetRequiredService<IOptions<SchemataTenancyOptions>>();
        var cache = new MemoryCacheTenantProviderCache(tenancy);
        var scopes = new SchemataTenantServiceScopeFactory<SchemataTenant>(
            root, new SchemataTenantServiceProviderFactory<SchemataTenant>(root, cache, tenancy));
        return new(root, cache, await scopes.CreateAsync(new(tenant)));
    }

    private static ITenantManager<SchemataTenant> Manager() {
        var tenants = new ConcurrentDictionary<Guid, SchemataTenant>();
        var manager = new Mock<ITenantManager<SchemataTenant>>();
        manager.Setup(value => value.FindByTenantId(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid identifier, CancellationToken _) => new ValueTask<SchemataTenant?>(
                tenants.GetOrAdd(identifier, static value => new SchemataTenant { Uid = value, Timestamp = Guid.NewGuid(), Name = value.ToString() })));
        return manager.Object;
    }

    private sealed class ContinuationScopes(
        ServiceProvider                                        root,
        MemoryCacheTenantProviderCache                         cache,
        AsyncServiceScope                                      issuing,
        AsyncServiceScope                                      sibling
    ) : IAsyncDisposable
    {
        internal const string IssuingTenant = "11111111-1111-1111-1111-111111111111";
        internal const string SiblingTenant = "22222222-2222-2222-2222-222222222222";

        internal IServiceProvider Issuing { get; } = issuing.ServiceProvider;

        internal IServiceProvider Sibling { get; } = sibling.ServiceProvider;

        public async ValueTask DisposeAsync() {
            await issuing.DisposeAsync();
            await sibling.DisposeAsync();
            await cache.DisposeAsync();
            await root.DisposeAsync();
        }
    }

    private sealed class ContinuationHost(
        ServiceProvider                root,
        MemoryCacheTenantProviderCache? cache,
        AsyncServiceScope?             scope
    ) : IAsyncDisposable
    {
        internal IServiceProvider Provider { get; } = scope?.ServiceProvider ?? root;

        public async ValueTask DisposeAsync() {
            if (scope is not null) await scope.Value.DisposeAsync();
            if (cache is not null) await cache.DisposeAsync();
            await root.DisposeAsync();
        }
    }

    private static Mock<IReportSnapshotStore> Store() {
        var store = new Mock<IReportSnapshotStore>();
        store.Setup(value => value.GetAsync(Snapshot, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchemataReportSnapshot { ChunkCount = 2 });
        store.Setup(value => value.GetChunkAsync(Snapshot, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, int index, CancellationToken _) => index switch {
                0 => new SchemataReportSnapshotChunk { Rows = "[{\"value\":1},{\"value\":2},{\"value\":3}]" },
                1 => new SchemataReportSnapshotChunk { Rows = "[{\"value\":4},{\"value\":5}]" },
                _ => null,
            });
        return store;
    }

    private static int Value(IReadOnlyDictionary<string, object?> row) => ((JsonElement)row["value"]!).GetInt32();

    private static AdviceContext Advice() => new(new ServiceCollection().BuildServiceProvider());
}
