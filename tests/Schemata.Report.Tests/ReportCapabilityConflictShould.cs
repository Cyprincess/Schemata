using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Core;
using Schemata.Report.Foundation;
using Schemata.Report.Foundation.Advisors;
using Schemata.Report.Foundation.Commands;
using Schemata.Report.Foundation.Definitions;
using Schemata.Report.Foundation.Handlers;
using Schemata.Report.Foundation.Jobs;
using Schemata.Report.Foundation.Queries;
using Schemata.Report.Foundation.Snapshots;
using Schemata.Report.Skeleton;
using Schemata.Report.Skeleton.Entities;
using Schemata.Report.Skeleton.Enums;
using Schemata.Report.Skeleton.Models;
using Xunit;

namespace Schemata.Report.Tests;

/// <summary>
///     Issue #65 observable matrix: a host registering conflicting Report entity triples still
///     builds, and every supported Report capability entry rejects with FAILED_PRECONDITION
///     before performing business I/O — the facade service/store, resource standard operations,
///     the custom snapshot-read command, the scheduled job, the snapshot writer, definition
///     reads, and retention enforcement.
/// </summary>
public class ReportCapabilityConflictShould
{
    [Abstractions.Entities.CanonicalName("reports/{report}")]
    private sealed class ConflictingReport : SchemataReport;

    [Abstractions.Entities.CanonicalName("reports/{report}/snapshots/{snapshot}")]
    private sealed class ConflictingSnapshot : SchemataReportSnapshot;

    [Abstractions.Entities.CanonicalName("reports/{report}/snapshots/{snapshot}/chunks/{chunk}")]
    private sealed class ConflictingChunk : SchemataReportSnapshotChunk;

    private static (ServiceProvider Provider, ReportRegistration Registration) ConflictHost() {
        var services = new ServiceCollection();
        var schemata  = new SchemataOptions();

        services.AddSchemataReport<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>(schemata);
        services.AddSchemataReport<ConflictingReport, ConflictingSnapshot, ConflictingChunk>(schemata);

        var provider = services.BuildServiceProvider();
        return (provider, provider.GetRequiredService<ReportRegistration>());
    }

    private static ServiceProvider EmptyProvider() {
        return new ServiceCollection().BuildServiceProvider();
    }

    private static async Task<TResult> FirstAsync<TResult>(IAsyncEnumerable<TResult> stream) {
        await foreach (var item in stream) {
            return item;
        }

        throw new InvalidOperationException("The stream produced no element.");
    }


    [Fact]
    public async Task FacadeService_RejectsRunAndGenerate_BeforeDispatch() {
        var (provider, _) = ConflictHost();
        using var _ = provider;

        var service = provider.GetRequiredService<IReportService>();

        await Assert.ThrowsAsync<FailedPreconditionException>(
            () => service.RunAsync(new() { Name = "reports/daily" }).AsTask());
        await Assert.ThrowsAsync<FailedPreconditionException>(
            () => service.GenerateAsync(new() { Name = "reports/daily" }).AsTask());
    }

    [Fact]
    public async Task FacadeStore_RejectsEverySnapshotRead_BeforeRepositoryUse() {
        var (provider, _) = ConflictHost();
        using var _ = provider;

        var store = provider.GetRequiredService<IReportSnapshotStore>();

        await Assert.ThrowsAsync<FailedPreconditionException>(
            () => store.GetAsync("reports/daily/snapshots/s1").AsTask());
        await Assert.ThrowsAsync<FailedPreconditionException>(
            () => store.GetChunkAsync("reports/daily/snapshots/s1", 0).AsTask());
        await Assert.ThrowsAsync<FailedPreconditionException>(
            () => FirstAsync(store.ListAsync("reports/daily")));
        await Assert.ThrowsAsync<FailedPreconditionException>(
            () => FirstAsync(store.ReadRowsAsync("reports/daily/snapshots/s1")));
    }

    [Fact]
    public async Task RegisteredResourceAdvisors_RejectEveryStandardCrudLane() {
        var (provider, _) = ConflictHost();
        using var _2 = provider;
        var ctx = new AdviceContext(provider);

        // The five registered Report lanes and the five registered Snapshot lanes all carry the
        // capability guard: the resource pipeline consults them before repository reads/writes.
        await Assert.ThrowsAsync<FailedPreconditionException>(() => provider
            .GetRequiredService<Schemata.Resource.Foundation.Advisors.IResourceGetRequestAdvisor<SchemataReport>>()
            .AdviseAsync(ctx, new() { Name = "reports/daily" }, new(), null));
        await Assert.ThrowsAsync<FailedPreconditionException>(() => provider
            .GetRequiredService<Schemata.Resource.Foundation.Advisors.IResourceListRequestAdvisor<SchemataReport>>()
            .AdviseAsync(ctx, new(), new(), null));
        await Assert.ThrowsAsync<FailedPreconditionException>(() => provider
            .GetRequiredService<Schemata.Resource.Foundation.Advisors.IResourceCreateRequestAdvisor<SchemataReport, SchemataReport>>()
            .AdviseAsync(ctx, new() { Name = "daily", CanonicalName = "reports/daily" }, new(), null));
        await Assert.ThrowsAsync<FailedPreconditionException>(() => provider
            .GetRequiredService<Schemata.Resource.Foundation.Advisors.IResourceUpdateRequestAdvisor<SchemataReport, SchemataReport>>()
            .AdviseAsync(ctx, new() { Name = "daily", CanonicalName = "reports/daily" }, new(), null));
        await Assert.ThrowsAsync<FailedPreconditionException>(() => provider
            .GetRequiredService<Schemata.Resource.Foundation.Advisors.IResourceDeleteRequestAdvisor<SchemataReport>>()
            .AdviseAsync(ctx, new() { Name = "reports/daily" }, new(), null));

        await Assert.ThrowsAsync<FailedPreconditionException>(() => provider
            .GetRequiredService<Schemata.Resource.Foundation.Advisors.IResourceGetRequestAdvisor<SchemataReportSnapshot>>()
            .AdviseAsync(ctx, new() { Name = "reports/daily/snapshots/s1" }, new(), null));
        await Assert.ThrowsAsync<FailedPreconditionException>(() => provider
            .GetRequiredService<Schemata.Resource.Foundation.Advisors.IResourceListRequestAdvisor<SchemataReportSnapshot>>()
            .AdviseAsync(ctx, new(), new(), null));
        await Assert.ThrowsAsync<FailedPreconditionException>(() => provider
            .GetRequiredService<Schemata.Resource.Foundation.Advisors.IResourceCreateRequestAdvisor<SchemataReportSnapshot, SchemataReportSnapshot>>()
            .AdviseAsync(ctx, new() { Name = "s1", CanonicalName = "reports/daily/snapshots/s1" }, new(), null));
        await Assert.ThrowsAsync<FailedPreconditionException>(() => provider
            .GetRequiredService<Schemata.Resource.Foundation.Advisors.IResourceUpdateRequestAdvisor<SchemataReportSnapshot, SchemataReportSnapshot>>()
            .AdviseAsync(ctx, new() { Name = "s1", CanonicalName = "reports/daily/snapshots/s1" }, new(), null));
        await Assert.ThrowsAsync<FailedPreconditionException>(() => provider
            .GetRequiredService<Schemata.Resource.Foundation.Advisors.IResourceDeleteRequestAdvisor<SchemataReportSnapshot>>()
            .AdviseAsync(ctx, new() { Name = "reports/daily/snapshots/s1" }, new(), null));
    }

    [Fact]
    public async Task SecondTripleAdvisors_RejectEveryLane() {
        var (provider, _) = ConflictHost();
        using var _2 = provider;
        var ctx = new AdviceContext(provider);

        // The conflicting triple's resource pipeline carries the same capability guard on all
        // thirteen lanes, so it also rejects before repository I/O.
        await Assert.ThrowsAsync<FailedPreconditionException>(() => provider
            .GetRequiredService<Schemata.Resource.Foundation.Advisors.IResourceGetRequestAdvisor<ConflictingReport>>()
            .AdviseAsync(ctx, new() { Name = "reports/daily" }, new(), null));
        await Assert.ThrowsAsync<FailedPreconditionException>(() => provider
            .GetRequiredService<Schemata.Resource.Foundation.Advisors.IResourceListRequestAdvisor<ConflictingReport>>()
            .AdviseAsync(ctx, new(), new(), null));
        await Assert.ThrowsAsync<FailedPreconditionException>(() => provider
            .GetRequiredService<Schemata.Resource.Foundation.Advisors.IResourceCreateRequestAdvisor<ConflictingReport, ConflictingReport>>()
            .AdviseAsync(ctx, new() { Name = "daily", CanonicalName = "reports/daily" }, new(), null));
        await Assert.ThrowsAsync<FailedPreconditionException>(() => provider
            .GetRequiredService<Schemata.Resource.Foundation.Advisors.IResourceUpdateRequestAdvisor<ConflictingReport, ConflictingReport>>()
            .AdviseAsync(ctx, new() { Name = "daily", CanonicalName = "reports/daily" }, new(), null));
        await Assert.ThrowsAsync<FailedPreconditionException>(() => provider
            .GetRequiredService<Schemata.Resource.Foundation.Advisors.IResourceDeleteRequestAdvisor<ConflictingReport>>()
            .AdviseAsync(ctx, new() { Name = "reports/daily" }, new(), null));

        await Assert.ThrowsAsync<FailedPreconditionException>(() => provider
            .GetRequiredService<Schemata.Resource.Foundation.Advisors.IResourceGetRequestAdvisor<ConflictingSnapshot>>()
            .AdviseAsync(ctx, new() { Name = "reports/daily/snapshots/s1" }, new(), null));
        await Assert.ThrowsAsync<FailedPreconditionException>(() => provider
            .GetRequiredService<Schemata.Resource.Foundation.Advisors.IResourceListRequestAdvisor<ConflictingSnapshot>>()
            .AdviseAsync(ctx, new(), new(), null));
        await Assert.ThrowsAsync<FailedPreconditionException>(() => provider
            .GetRequiredService<Schemata.Resource.Foundation.Advisors.IResourceCreateRequestAdvisor<ConflictingSnapshot, ConflictingSnapshot>>()
            .AdviseAsync(ctx, new() { Name = "s1", CanonicalName = "reports/daily/snapshots/s1" }, new(), null));
        await Assert.ThrowsAsync<FailedPreconditionException>(() => provider
            .GetRequiredService<Schemata.Resource.Foundation.Advisors.IResourceUpdateRequestAdvisor<ConflictingSnapshot, ConflictingSnapshot>>()
            .AdviseAsync(ctx, new() { Name = "s1", CanonicalName = "reports/daily/snapshots/s1" }, new(), null));
        await Assert.ThrowsAsync<FailedPreconditionException>(() => provider
            .GetRequiredService<Schemata.Resource.Foundation.Advisors.IResourceDeleteRequestAdvisor<ConflictingSnapshot>>()
            .AdviseAsync(ctx, new() { Name = "reports/daily/snapshots/s1" }, new(), null));

        await Assert.ThrowsAsync<FailedPreconditionException>(() => provider
            .GetRequiredService<Schemata.Resource.Foundation.Advisors.IResourceMethodRequestAdvisor<ConflictingReport, RunReportRequest>>()
            .AdviseAsync(ctx, new(new() { Name = "reports/daily" }, null), new(), null));
        await Assert.ThrowsAsync<FailedPreconditionException>(() => provider
            .GetRequiredService<Schemata.Resource.Foundation.Advisors.IResourceMethodRequestAdvisor<ConflictingReport, GenerateReportRequest>>()
            .AdviseAsync(ctx, new() { Name = "reports/daily" }, new(), null));
        await Assert.ThrowsAsync<FailedPreconditionException>(() => provider
            .GetRequiredService<Schemata.Resource.Foundation.Advisors.IResourceMethodRequestAdvisor<ConflictingSnapshot, ReadSnapshotRequest>>()
            .AdviseAsync(ctx, new() { CanonicalName = "reports/daily/snapshots/s1" }, new(), null));
    }

    [Trait("Layer", "Component")]
    [Fact]
    public async Task Raw_Report_Commands_Reject_Conflict_Before_Handler_Construction() {
        var (provider, _) = ConflictHost();
        using var lifetime = provider;
        var dispatcher = provider.GetRequiredService<Schemata.Messaging.Skeleton.IRequestDispatcher>();

        await Assert.ThrowsAsync<FailedPreconditionException>(() => dispatcher.SendAsync<RunReportRequest, ReportResult>(
            new(new() { Name = "reports/daily" }, null)));
        await Assert.ThrowsAsync<FailedPreconditionException>(() => dispatcher.SendAsync<GenerateReportRequest, Operation>(
            new() { Name = "reports/daily" }));
        await Assert.ThrowsAsync<FailedPreconditionException>(() => dispatcher.SendAsync<ReadSnapshotRequest, ReadSnapshotResponse>(
            new() { CanonicalName = "reports/daily/snapshots/current" }));
    }

    [Fact]
    public async Task ScheduledJob_Rejects_BeforeReadingRequest() {
        var (_, registration) = ConflictHost();
        var job = new ReportGenerationJob<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>(
            new Mock<IServiceScopeFactory>().Object,
            Options.Create(new SchemataReportOptions()),
            registration);

        // The guard fires before the job even reads its staged request variables.
        await Assert.ThrowsAsync<FailedPreconditionException>(
            () => job.ExecuteAsync(new(), CancellationToken.None));
    }

    [Fact]
    public async Task SnapshotWriter_Rejects_BeforeCreatingHeaderOrMaterializing() {
        var (_, registration) = ConflictHost();
        var writer = new ReportSnapshotWriter<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>(
            new Mock<IServiceScopeFactory>().Object,
            registration,
            Options.Create(new SchemataReportOptions { ChunkSize = 10 }),
            new(
                new Mock<IServiceScopeFactory>().Object,
                Options.Create(new SchemataReportOptions()), registration));

        var materialized = false;
        await Assert.ThrowsAsync<FailedPreconditionException>(
            () => writer.WriteAsync(null, ReportRunKind.ImmediatePersisted, _ => {
                materialized = true;
                return ValueTask.FromResult<Insight.Foundation.Execution.MaterializedQuery>(null!);
            }).AsTask());
        Assert.False(materialized);
    }

    [Fact]
    public async Task DefinitionStore_RejectsDefinitionReads_BeforeSources() {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var (host, registration) = ConflictHost();
        using var lifetime = host;
        var store = new CompositeReportDefinitionStore(provider, registration);

        await Assert.ThrowsAsync<FailedPreconditionException>(
            () => store.ResolveAsync("reports/daily").AsTask());
        await Assert.ThrowsAsync<FailedPreconditionException>(
            () => FirstAsync(store.ListPeriodicAsync()));
    }

    [Fact]
    public async Task RetentionEnforcer_Rejects_BeforeListingSnapshots() {
        var (host, registration) = ConflictHost();
        using var lifetime = host;
        var scopes = new Mock<IServiceScopeFactory>();
        var enforcer = new ReportRetentionEnforcer<SchemataReportSnapshot, SchemataReportSnapshotChunk>(
            scopes.Object, Options.Create(new SchemataReportOptions()), registration);

        await Assert.ThrowsAsync<FailedPreconditionException>(
            () => enforcer.EnforceAsync(new() { Name = "daily", Retention = new() }).AsTask());
    }

    [Fact]
    public async Task SnapshotReadMethodEnvelope_Rejects_BeforeInstanceLoad() {
        var (_, registration) = ConflictHost();
        var repository = new Mock<Entity.Repository.IRepository<SchemataReportSnapshot>>(MockBehavior.Loose);
        var services = new ServiceCollection();
        services.AddScoped(_ => repository.Object);
        services.AddScoped(_ => registration);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            Schemata.Resource.Foundation.Advisors.IResourceMethodRequestAdvisor<SchemataReportSnapshot, ReadSnapshotRequest>,
            ReportEntityMethodRequestAdvisor<SchemataReportSnapshot, ReadSnapshotRequest>>());
        services.AddScoped<Schemata.Resource.Foundation.ResourceMethodOperationHandler<SchemataReportSnapshot, ReadSnapshotRequest, ReadSnapshotResponse>>();
        services.AddScoped<Schemata.Resource.Foundation.Handlers.ResourceMethodDispatchHandler<SchemataReportSnapshot, ReadSnapshotRequest, ReadSnapshotResponse>>();
        services.AddScoped<Messaging.Skeleton.IRequestDispatcher>(_ => null!);
        using var provider = services.BuildServiceProvider();

        using var ambient = AdviceContext.Establish(new(provider));
        // The real custom-method envelope: the dispatch handler unwraps the verb request and the
        // method-request advisor lane runs before the instance load touches the repository.
        var dispatcher = provider.GetRequiredService<Schemata.Resource.Foundation.Handlers.ResourceMethodDispatchHandler<SchemataReportSnapshot, ReadSnapshotRequest, ReadSnapshotResponse>>();

        await Assert.ThrowsAsync<FailedPreconditionException>(
            () => dispatcher.HandleAsync(new("read", "reports/daily/snapshots/s1",
                                            new() { CanonicalName = "reports/daily/snapshots/s1" }, null)));
        repository.VerifyNoOtherCalls();
    }
}
