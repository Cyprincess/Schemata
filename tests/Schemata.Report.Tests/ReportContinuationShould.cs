using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Tenancy;
using Schemata.Common;
using Schemata.Report.Foundation;
using Schemata.Report.Foundation.Handlers;
using Schemata.Report.Foundation.Queries;
using Schemata.Report.Skeleton;
using Schemata.Report.Skeleton.Entities;
using Xunit;

namespace Schemata.Report.Tests;

[Trait("Category", "Integration")]
public sealed class ReportContinuationShould
{
    private const string Snapshot = "reports/A/snapshots/daily";

    [Theory]
    [Trait("Layer", "Integration")]
    [InlineData("snapshot")]
    [InlineData("page-size")]
    [InlineData("subject")]
    [InlineData("tenant")]
    [InlineData("tamper")]
    [InlineData("truncate")]
    [InlineData("key-ring")]
    [InlineData("negative-chunk")]
    [InlineData("negative-offset")]
    [InlineData("unprotected")]
    public async Task Reject_Changed_Bindings_And_Invalid_Positions_Before_Store_IO(string change) {
        var protection = new EphemeralDataProtectionProvider();
        var protector = protection.CreateProtector(ReportReadPageToken.ProtectionPurpose);
        var subject = Principal("alice");
        var tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
        string token;
        using (TenantContext.Enter(new(tenant))) {
            var binding = ReportReadPageToken.Bind(Snapshot, 1, subject);
            token = change switch {
                "negative-chunk" => ProtectedContinuation.Encode(protector, binding with { ChunkIndex = -1 }),
                "negative-offset" => ProtectedContinuation.Encode(protector, binding with { Offset = -1 }),
                "unprotected" => Convert.ToBase64String(new byte[8]),
                _ => binding.Encode(protector, 0, 1),
            };
        }
        if (change == "tamper") token = (token[0] == 'A' ? "B" : "A") + token[1..];
        if (change == "truncate") token = token[..^8];
        var store = new Mock<IReportSnapshotStore>(MockBehavior.Strict);
        var handler = new ReadSnapshotHandler<SchemataReportSnapshot>(store.Object,
            Options.Create(new SchemataReportOptions()),
            change == "key-ring" ? new EphemeralDataProtectionProvider() : protection);
        using var context = TenantContext.Enter(new(change == "tenant" ? Guid.Parse("22222222-2222-2222-2222-222222222222") : tenant));
        var error = await Assert.ThrowsAsync<InvalidArgumentException>(() => handler.HandleAsync(new() {
            CanonicalName = change == "snapshot" ? "reports/B/snapshots/daily" : Snapshot,
            PageSize = change == "page-size" ? 2 : 1,
            Principal = change == "subject" ? Principal("bob") : subject,
            PageToken = token,
        }));
        Assert.Equal(SchemataResources.INVALID_PAGE_TOKEN, Assert.Single(error.Details!.OfType<ErrorInfoDetail>()).Reason);
        store.VerifyNoOtherCalls();
    }

    [Fact]
    [Trait("Layer", "Integration")]
    public async Task Continue_After_Key_Ring_Restart_With_Exact_Rows_And_Normalized_Page_Size() {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "report-continuation-" + Guid.NewGuid().ToString("N")));
        try {
            var store = Store();
            string? token;
            var rows = new List<int>();
            using (var firstHost = Host(directory)) {
                var handler = Handler(store.Object, firstHost.GetRequiredService<IDataProtectionProvider>());
                var principal = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, "alice"), new Claim("sub", "shadow"),
                ], "test"));
                var first = await handler.HandleAsync(new() { CanonicalName = Snapshot, PageSize = 100, Principal = principal });
                rows.AddRange(first.Rows.Select(Value));
                token = first.NextPageToken;
                Assert.NotNull(token);
            }
            using (var restartedHost = Host(directory)) {
                var handler = Handler(store.Object, restartedHost.GetRequiredService<IDataProtectionProvider>());
                do {
                    var page = await handler.HandleAsync(new() { Name = Snapshot, PageSize = 2, PageToken = token, Principal = Principal("alice", "sub") });
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
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => handler.HandleAsync(new() { Name = Snapshot })));
    }

    [Fact]
    [Trait("Layer", "Integration")]
    public async Task Reject_Chunk_Overflow_With_Canonical_Argument_Error() {
        var protection = new EphemeralDataProtectionProvider();
        var store = new Mock<IReportSnapshotStore>();
        store.Setup(value => value.GetAsync(Snapshot, It.IsAny<CancellationToken>())).ReturnsAsync(new SchemataReportSnapshot());
        store.Setup(value => value.GetChunkAsync(Snapshot, int.MaxValue, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchemataReportSnapshotChunk { Rows = "[{\"value\":1}]" });
        var token = ReportReadPageToken.Bind(Snapshot, 1, null)
            .Encode(protection.CreateProtector(ReportReadPageToken.ProtectionPurpose), int.MaxValue, 0);
        var handler = Handler(store.Object, protection);
        var error = await Assert.ThrowsAsync<InvalidArgumentException>(() => handler.HandleAsync(new() { Name = Snapshot, PageSize = 1, PageToken = token }));
        Assert.Equal(SchemataResources.INVALID_PAGE_TOKEN, Assert.Single(error.Details!.OfType<ErrorInfoDetail>()).Reason);
    }

    private static ClaimsPrincipal Principal(string subject, string claim = ClaimTypes.NameIdentifier) =>
        new(new ClaimsIdentity([new Claim(claim, subject)], "test"));

    private static ReadSnapshotHandler<SchemataReportSnapshot> Handler(IReportSnapshotStore store, IDataProtectionProvider protection) =>
        new(store, Options.Create(new SchemataReportOptions { MaxReadPageSize = 2 }), protection);

    private static ServiceProvider Host(DirectoryInfo directory) {
        var services = new ServiceCollection();
        services.AddDataProtection().PersistKeysToFileSystem(directory).SetApplicationName("ReportContinuationTests");
        return services.BuildServiceProvider();
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
}
