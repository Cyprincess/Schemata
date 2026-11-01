using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Core;
using Schemata.Report.Foundation;
using Schemata.Report.Skeleton;
using Schemata.Report.Skeleton.Entities;
using Schemata.Report.Skeleton.Models;
using Xunit;

namespace Schemata.Report.Tests;

public class ReportCustomImplementationShould
{
    [Trait("Layer", "Component")]
    [Fact]
    public async Task Builder_Last_Service_Selection_Executes_Only_The_Selected_Inner() {
        var services = new ServiceCollection();
        var options = new SchemataOptions();
        var builder = new SchemataReportBuilder<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>(options, services);
        builder.UseService<FirstService>().UseService<SecondService>();
        services.AddSchemataReport<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>(options);
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IReportService>().RunAsync(new() { Name = "reports/daily" });

        Assert.Equal("second", result.Snapshot);
        var generated = await provider.GetRequiredService<IReportService>().GenerateAsync(new() { Name = "reports/daily" });
        Assert.Equal("operations/second", generated.CanonicalName);
    }

    [Trait("Layer", "Component")]
    [Fact]
    public async Task Conflicting_Selection_Rejects_Custom_Service_Before_Construction() {
        var services = new ServiceCollection();
        var options = new SchemataOptions();
        new SchemataReportBuilder<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>(options, services)
            .UseService<FirstService>();
        services.AddSchemataReport<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>(options);
        services.AddSchemataReport<OtherReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>(options);
        using var provider = services.BuildServiceProvider();

        await Assert.ThrowsAsync<FailedPreconditionException>(() => provider.GetRequiredService<IReportService>()
            .RunAsync(new() { Name = "reports/daily" }).AsTask());
    }

    [Trait("Layer", "Component")]
    [Fact]
    public async Task Conflicting_Selection_Rejects_Custom_Store_Before_Construction() {
        var services = new ServiceCollection();
        var options = new SchemataOptions();
        new SchemataReportBuilder<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>(options, services)
            .UseSnapshotStore<ThrowingStore>();
        services.AddSchemataReport<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>(options);
        services.AddSchemataReport<OtherReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>(options);
        using var provider = services.BuildServiceProvider();

        await Assert.ThrowsAsync<FailedPreconditionException>(() => provider.GetRequiredService<IReportSnapshotStore>()
            .GetAsync("reports/daily/snapshots/current").AsTask());
    }

    [Trait("Layer", "Component")]
    [Fact]
    public async Task Builder_Last_Store_Selection_Executes_Only_The_Selected_Inner() {
        var services = new ServiceCollection();
        var options = new SchemataOptions();
        var builder = new SchemataReportBuilder<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>(options, services);
        builder.UseSnapshotStore<ThrowingStore>().UseSnapshotStore<SelectedStore>();
        services.AddSchemataReport<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>(options);
        using var provider = services.BuildServiceProvider();

        var header = await provider.GetRequiredService<IReportSnapshotStore>().GetAsync("reports/daily/snapshots/current");

        Assert.Equal("selected-store", header?.Error);
    }

    [Trait("Layer", "Component")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Direct_Store_Override_Owns_The_Contract_Without_Descriptor_Cleanup(bool late) {
        var services = new ServiceCollection();
        if (!late) services.AddScoped<IReportSnapshotStore, SelectedStore>();
        services.AddSchemataReport<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>(new());
        if (late) services.AddScoped<IReportSnapshotStore, SelectedStore>();
        using var provider = services.BuildServiceProvider();

        var header = await provider.GetRequiredService<IReportSnapshotStore>().GetAsync("reports/daily/snapshots/current");

        Assert.Equal("selected-store", header?.Error);
    }

    [Abstractions.Entities.CanonicalName("reports/{report}")]
    private sealed class OtherReport : SchemataReport;

    private sealed class FirstService : IReportService
    {
        public FirstService() => throw new InvalidOperationException("Unselected or conflicting service was constructed.");
        public ValueTask<ReportResult> RunAsync(ReportRequest request, System.Security.Claims.ClaimsPrincipal? principal = null, CancellationToken ct = default)
            => throw new InvalidOperationException("Unselected service executed.");
        public ValueTask<Abstractions.Resource.Operation> GenerateAsync(ReportRequest request, CancellationToken ct = default)
            => throw new InvalidOperationException("Unselected service executed.");
    }

    private sealed class SecondService : IReportService
    {
        public ValueTask<ReportResult> RunAsync(ReportRequest request, System.Security.Claims.ClaimsPrincipal? principal = null, CancellationToken ct = default)
            => ValueTask.FromResult(new ReportResult { Snapshot = "second" });
        public ValueTask<Abstractions.Resource.Operation> GenerateAsync(ReportRequest request, CancellationToken ct = default)
            => ValueTask.FromResult(new Abstractions.Resource.Operation { CanonicalName = "operations/second" });
    }

    private sealed class SelectedStore : IReportSnapshotStore
    {
        public System.Collections.Generic.IAsyncEnumerable<SchemataReportSnapshot> ListAsync(string reportName, CancellationToken ct = default)
            => throw new InvalidOperationException();
        public ValueTask<SchemataReportSnapshot?> GetAsync(string snapshotName, CancellationToken ct = default)
            => ValueTask.FromResult<SchemataReportSnapshot?>(new() { CanonicalName = snapshotName, Error = "selected-store" });
        public ValueTask<SchemataReportSnapshotChunk?> GetChunkAsync(string snapshotName, int index, CancellationToken ct = default)
            => throw new InvalidOperationException();
        public System.Collections.Generic.IAsyncEnumerable<System.Collections.Generic.IReadOnlyDictionary<string, object?>> ReadRowsAsync(string snapshotName, CancellationToken ct = default)
            => throw new InvalidOperationException();
    }

    private sealed class ThrowingStore : IReportSnapshotStore
    {
        public ThrowingStore() => throw new InvalidOperationException("Conflicting store was constructed.");
        public System.Collections.Generic.IAsyncEnumerable<SchemataReportSnapshot> ListAsync(string reportName, CancellationToken ct = default)
            => throw new InvalidOperationException();
        public ValueTask<SchemataReportSnapshot?> GetAsync(string snapshotName, CancellationToken ct = default)
            => throw new InvalidOperationException();
        public ValueTask<SchemataReportSnapshotChunk?> GetChunkAsync(string snapshotName, int index, CancellationToken ct = default)
            => throw new InvalidOperationException();
        public System.Collections.Generic.IAsyncEnumerable<System.Collections.Generic.IReadOnlyDictionary<string, object?>> ReadRowsAsync(string snapshotName, CancellationToken ct = default)
            => throw new InvalidOperationException();
    }
}
