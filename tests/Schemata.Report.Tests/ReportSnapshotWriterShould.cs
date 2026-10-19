using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Report.Foundation;
using Schemata.Report.Foundation.Snapshots;
using Schemata.Report.Skeleton.Entities;
using Schemata.Report.Skeleton.Enums;
using Xunit;

using Schemata.Abstractions.Advisors;
namespace Schemata.Report.Tests;

[Trait("Layer", "Unit")]
public sealed class ReportSnapshotWriterShould : IDisposable
{
    private static readonly ReportRegistration Registration = new(
        typeof(SchemataReport), typeof(SchemataReportSnapshot), typeof(SchemataReportSnapshotChunk));

    private readonly Mock<IResourceMutation<SchemataReportSnapshot>>        _headers = new();
    private readonly Mock<IResourceMutation<SchemataReportSnapshotChunk>>  _chunks  = new();
    private readonly ServiceProvider                                       _provider;
    private readonly List<SnapshotState>                                   _creates = [];
    private readonly List<(SnapshotState State, string? Error, bool Cancelled)> _updates = [];

    private Exception? _createFailure;
    private Exception? _runningFailure;

    public ReportSnapshotWriterShould() {
        var services = new ServiceCollection();
        services.AddScoped<IResourceMutation<SchemataReportSnapshot>>(_ => _headers.Object);
        services.AddScoped<IResourceMutation<SchemataReportSnapshotChunk>>(_ => _chunks.Object);
        _provider = services.BuildServiceProvider();
        _headers.Setup(value => value.CreateAsync(
                      It.IsAny<SchemataReportSnapshot>(), It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>()))
                 .Returns((SchemataReportSnapshot header, IUnitOfWork? _, CancellationToken _) => {
                     if (_createFailure is { } failure) {
                         return Task.FromException<MutationResult>(failure);
                     }

                     _creates.Add(header.State);
                     return Task.FromResult(MutationResult.Applied);
                 });
        _headers.Setup(value => value.UpdateAsync(
                      It.IsAny<SchemataReportSnapshot>(), It.IsAny<IUnitOfWork?>(), It.IsAny<Operations>(),
                      It.IsAny<CancellationToken>()))
                 .Returns((SchemataReportSnapshot header, IUnitOfWork? _, Operations _, CancellationToken ct) => {
                     if (_runningFailure is { } failure) {
                         _runningFailure = null;
                         return Task.FromException<MutationResult>(failure);
                     }

                     _updates.Add((header.State, header.Error, ct.IsCancellationRequested));
                     return Task.FromResult(MutationResult.Applied);
                 });
    }

    [Fact]
    public async Task Settle_Committed_Pending_Header_Failed_When_Running_Update_Fails() {
        _runningFailure = new InvalidOperationException("mutation-dependency");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Writer().WriteAsync(
                null, ReportRunKind.ImmediatePersisted,
                static _ => throw new UnreachableException("materialize must not run before the Running transition"),
                new AdviceContext(_provider)).AsTask());

        Assert.Equal("mutation-dependency", failure.Message);
        Assert.Equal([SnapshotState.Pending], _creates);
        var settlement = Assert.Single(_updates);
        Assert.Equal(SnapshotState.Failed, settlement.State);
        Assert.Equal("mutation-dependency", settlement.Error);
        Assert.False(settlement.Cancelled);
    }

    [Fact]
    public async Task Settle_Committed_Pending_Header_Cancelled_When_Running_Update_Is_Cancelled() {
        _runningFailure = new OperationCanceledException();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Writer().WriteAsync(
                null, ReportRunKind.ImmediatePersisted,
                static _ => throw new UnreachableException("materialize must not run before the Running transition"),
                new AdviceContext(_provider)).AsTask());

        Assert.Equal([SnapshotState.Pending], _creates);
        var settlement = Assert.Single(_updates);
        Assert.Equal(SnapshotState.Cancelled, settlement.State);
        Assert.Null(settlement.Error);
        Assert.False(settlement.Cancelled);
    }

    [Fact]
    public async Task Leave_No_Failed_Settlement_When_Header_Creation_Fails() {
        _createFailure = new IOException("snapshot-store-down");

        var failure = await Assert.ThrowsAsync<IOException>(
            () => Writer().WriteAsync(
                null, ReportRunKind.ImmediatePersisted,
                static _ => throw new UnreachableException("materialize must not run before the header exists"),
                new AdviceContext(_provider)).AsTask());

        Assert.Equal("snapshot-store-down", failure.Message);
        Assert.Empty(_creates);
        Assert.Empty(_updates);
        _chunks.VerifyNoOtherCalls();
    }

    private ReportSnapshotWriter<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk> Writer() {
        var options = Options.Create(new SchemataReportOptions { ChunkSize = 2 });
        var scopes  = _provider.GetRequiredService<IServiceScopeFactory>();
        return new(scopes, Registration, options,
            new ReportRetentionEnforcer<SchemataReportSnapshot, SchemataReportSnapshotChunk>(scopes, options, Registration));
    }

    public void Dispose() {
        _provider.Dispose();
    }
}
