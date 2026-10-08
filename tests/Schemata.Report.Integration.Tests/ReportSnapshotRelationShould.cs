using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Messaging.Skeleton;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Report.Foundation.Queries;
using Schemata.Report.Foundation.Snapshots;
using Schemata.Report.Integration.Tests.Fixtures;
using Schemata.Report.Skeleton;
using Schemata.Report.Skeleton.Entities;
using Schemata.Report.Skeleton.Enums;
using Xunit;

namespace Schemata.Report.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class ReportSnapshotRelationShould
{
    [Trait("Layer", "Integration")]
    [Fact]
    public async Task Read_Daily_Snapshots_With_The_Same_Leaf_Only_From_Their_Owning_Report() {
        await using var fixture = new SnapshotRelationFixture();
        await fixture.CreateAsync();
        using var scope = fixture.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IReportSnapshotStore>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();

        foreach (var (report, expected) in new[] { ("A", new[] { 1, 2, 3 }), ("B", new[] { 101, 102, 103 }) }) {
            var name = $"reports/{report}/snapshots/daily";
            var listed = new List<SchemataReportSnapshot>();
            await foreach (var snapshot in store.ListAsync($"reports/{report}")) listed.Add(snapshot);
            Assert.Equal(name, Assert.Single(listed).CanonicalName);
            var header = await store.GetAsync(name);
            Assert.NotNull(header);
            Assert.Equal(name, header.CanonicalName);
            Assert.Equal(report, header.Report);
            Assert.Equal("daily", header.Name);
            Assert.Equal(SnapshotState.Succeeded, header.State);
            Assert.Equal(3, header.RowCount);
            Assert.Equal(2, header.ChunkCount);
            var streamed = new List<int>();
            await foreach (var row in store.ReadRowsAsync(name)) streamed.Add(Value(row));
            Assert.Equal(expected, streamed);

            for (var index = 0; index < header.ChunkCount; index++) {
                var chunk = await store.GetChunkAsync(name, index);
                Assert.NotNull(chunk);
                Assert.Equal(report, chunk.Report);
                Assert.Equal("daily", chunk.Snapshot);
                Assert.Equal($"{name}/chunks/page-{index}", chunk.CanonicalName);
                Assert.Equal(expected.Skip(index * 2).Take(2),
                             JsonSerializer.Deserialize<JsonElement>(chunk.Rows!).EnumerateArray()
                                           .Select(row => row.GetProperty("value").GetInt32()));
            }
            Assert.Null(await store.GetChunkAsync(name, 2));

            var first = await dispatcher.SendAsync<ReadSnapshotRequest, ReadSnapshotResponse>(new() {
                CanonicalName = name, PageSize = 1,
            });
            Assert.Equal(expected.Take(1), first.Rows.Select(Value));
            Assert.NotNull(first.NextPageToken);
            var second = await dispatcher.SendAsync<ReadSnapshotRequest, ReadSnapshotResponse>(new() {
                CanonicalName = name, PageSize = 1, PageToken = first.NextPageToken,
            });
            Assert.Equal(expected.Skip(1).Take(1), second.Rows.Select(Value));
            Assert.NotNull(second.NextPageToken);
            var third = await dispatcher.SendAsync<ReadSnapshotRequest, ReadSnapshotResponse>(new() {
                CanonicalName = name, PageSize = 1, PageToken = second.NextPageToken,
            });
            Assert.Equal(expected.Skip(2), third.Rows.Select(Value));
            Assert.Null(third.NextPageToken);
        }
        Assert.Null(await store.GetAsync("reports/C/snapshots/daily"));
        Assert.Null(await store.GetChunkAsync("reports/C/snapshots/daily", 0));
        var missingRows = new List<IReadOnlyDictionary<string, object?>>();
        await foreach (var row in store.ReadRowsAsync("reports/C/snapshots/daily")) missingRows.Add(row);
        Assert.Empty(missingRows);
    }

    [Trait("Layer", "Integration")]
    [Theory]
    [InlineData("daily")]
    [InlineData("reports/A/snapshot/daily")]
    [InlineData("reports//snapshots/daily")]
    [InlineData("reports/A/snapshots/")]
    [InlineData("reports/A/snapshots/-")]
    [InlineData("reports/-/snapshots/daily")]
    public async Task Reject_Malformed_Snapshot_Targets_With_Structured_Argument_Errors(string name) {
        await using var fixture = new SnapshotRelationFixture();
        using var scope = fixture.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IReportSnapshotStore>();
        var header = await Assert.ThrowsAsync<InvalidArgumentException>(async () => await store.GetAsync(name));
        var chunk = await Assert.ThrowsAsync<InvalidArgumentException>(async () => await store.GetChunkAsync(name, 0));
        var rows = await Assert.ThrowsAsync<InvalidArgumentException>(async () => {
            await foreach (var row in store.ReadRowsAsync(name)) { Assert.Fail("Malformed targets cannot produce rows."); }
        });
        var dispatcher = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
        var page = await Assert.ThrowsAsync<InvalidArgumentException>(async () =>
            await dispatcher.SendAsync<ReadSnapshotRequest, ReadSnapshotResponse>(new() { CanonicalName = name }));
        foreach (var error in new[] { header, chunk, rows, page }) {
            Assert.Equal(SchemataResources.INVALID_NAME, Assert.Single(error.Details!.OfType<ErrorInfoDetail>()).Reason);
        }
    }

    [Trait("Layer", "Integration")]
    [Theory]
    [InlineData("A")]
    [InlineData("reports/")]
    [InlineData("reports/-")]
    [InlineData("reports/A/snapshots/daily")]
    public async Task Reject_Malformed_Report_List_Targets_With_Structured_Argument_Errors(string name) {
        await using var fixture = new SnapshotRelationFixture();
        using var scope = fixture.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IReportSnapshotStore>();
        var error = await Assert.ThrowsAsync<InvalidArgumentException>(async () => {
            await foreach (var snapshot in store.ListAsync(name)) { Assert.Fail("Malformed targets cannot produce headers."); }
        });
        Assert.Equal(SchemataResources.INVALID_NAME, Assert.Single(error.Details!.OfType<ErrorInfoDetail>()).Reason);
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task Retain_Only_The_Eligible_Parent_And_Preserve_Sibling_Daily_Chunks() {
        await using var fixture = new SnapshotRelationFixture();
        await fixture.CreateAsync();
        var report = await fixture.RetainAsync("A", new() { MaxCount = 0 });
        using (var scope = fixture.Services.CreateScope()) {
            await scope.ServiceProvider.GetRequiredService<ReportRetentionEnforcer<SchemataReportSnapshot, SchemataReportSnapshotChunk>>()
                       .EnforceAsync(report);
        }

        using var verification = fixture.Services.CreateScope();
        var database = verification.ServiceProvider.GetRequiredService<TestDbContext>();
        var header = Assert.Single(await database.ReportSnapshots.AsNoTracking().ToListAsync());
        Assert.Equal("reports/B/snapshots/daily", header.CanonicalName);
        var chunks = await database.ReportSnapshotChunks.AsNoTracking().OrderBy(row => row.Index).ToListAsync();
        Assert.Equal(new[] { "reports/B/snapshots/daily/chunks/page-0", "reports/B/snapshots/daily/chunks/page-1" },
                     chunks.Select(row => row.CanonicalName));
        var store = verification.ServiceProvider.GetRequiredService<IReportSnapshotStore>();
        Assert.Null(await store.GetAsync("reports/A/snapshots/daily"));
        Assert.Null(await store.GetChunkAsync("reports/A/snapshots/daily", 0));
        var rows = new List<int>();
        await foreach (var row in store.ReadRowsAsync(header.CanonicalName!)) rows.Add(Value(row));
        Assert.Equal(new[] { 101, 102, 103 }, rows);
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task Roll_Back_Chunk_Deletions_When_The_Victim_Header_Delete_Fails() {
        var failure = new InvalidOperationException("retention-header-failure");
        var advisor = new Mock<IRepositoryRemoveAdvisor<SchemataReportSnapshot>>();
        advisor.Setup(item => item.AdviseAsync(
                   It.IsAny<AdviceContext>(), It.IsAny<IRepository<SchemataReportSnapshot>>(),
                   It.IsAny<SchemataReportSnapshot>(), It.IsAny<System.Threading.CancellationToken>()))
               .ThrowsAsync(failure);
        await using var fixture = new SnapshotRelationFixture(services => services.AddScoped(_ => advisor.Object));
        await fixture.CreateAsync();
        var report = await fixture.RetainAsync("A", new() { MaxCount = 0 });
        using (var scope = fixture.Services.CreateScope()) {
            var observed = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await scope.ServiceProvider.GetRequiredService<ReportRetentionEnforcer<SchemataReportSnapshot, SchemataReportSnapshotChunk>>()
                           .EnforceAsync(report));
            Assert.Same(failure, observed);
        }

        using var verification = fixture.Services.CreateScope();
        var database = verification.ServiceProvider.GetRequiredService<TestDbContext>();
        Assert.Equal(new[] { "reports/A/snapshots/daily", "reports/B/snapshots/daily" },
                     (await database.ReportSnapshots.AsNoTracking().OrderBy(row => row.CanonicalName).ToListAsync())
                     .Select(row => row.CanonicalName));
        Assert.Equal(new[] {
            "reports/A/snapshots/daily/chunks/page-0", "reports/A/snapshots/daily/chunks/page-1",
            "reports/B/snapshots/daily/chunks/page-0", "reports/B/snapshots/daily/chunks/page-1",
        }, (await database.ReportSnapshotChunks.AsNoTracking().OrderBy(row => row.CanonicalName).ToListAsync())
           .Select(row => row.CanonicalName));
        var store = verification.ServiceProvider.GetRequiredService<IReportSnapshotStore>();
        foreach (var (parent, expected) in new[] { ("A", new[] { 1, 2, 3 }), ("B", new[] { 101, 102, 103 }) }) {
            var rows = new List<int>();
            await foreach (var row in store.ReadRowsAsync($"reports/{parent}/snapshots/daily")) rows.Add(Value(row));
            Assert.Equal(expected, rows);
        }
    }

    private static int Value(IReadOnlyDictionary<string, object?> row) => ((JsonElement)row["value"]!).GetInt32();
}
