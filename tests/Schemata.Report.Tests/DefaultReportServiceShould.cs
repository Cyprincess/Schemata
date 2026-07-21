using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Insight.Skeleton;
using Schemata.Report.Skeleton;
using Xunit;

namespace Schemata.Report.Tests;

public class DefaultReportServiceShould
{
    [Fact]
    public async Task Run_Inline_Returns_Rows_Under_Cap() {
        var driver = ReportTestHost.CreateDriver(ReportTestRows.Create(3));
        using var provider = ReportTestHost.Create(driver, maxInlineRows: 3);
        var service = provider.GetRequiredService<IReportService>();

        var result = await service.RunAsync(ReportTestHost.InlineRequest());

        Assert.Null(result.Snapshot);
        Assert.Equal(3, result.Response.Rows.Count);
        Assert.Equal(3, result.Response.TotalSize);
    }

    [Fact]
    public async Task Run_Inline_Beyond_MaxInlineRows_Throws_With_Persist_Hint() {
        var driver = ReportTestHost.CreateDriver(ReportTestRows.Create(3));
        using var provider = ReportTestHost.Create(driver, maxInlineRows: 2);
        var service = provider.GetRequiredService<IReportService>();

        var exception = await Assert.ThrowsAsync<ReportException>(async () => {
            await service.RunAsync(ReportTestHost.InlineRequest());
        });

        Assert.Contains("Persist=true", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_Persist_Writes_Header_And_Chunks_With_Fresh_Scope_Per_Chunk() {
        var state = new ReportPersistenceState();
        var driver = ReportTestHost.CreateDriver(ReportTestRows.Create(5));
        using var provider = ReportTestHost.Create(driver, state, chunkSize: 2);
        var service = provider.GetRequiredService<IReportService>();

        var result = await service.RunAsync(ReportTestHost.InlineRequest(persist: true));

        var snapshot = Assert.Single(state.Snapshots);
        Assert.Equal(SnapshotState.Succeeded, snapshot.State);
        Assert.Equal(5, snapshot.RowCount);
        Assert.Equal(3, snapshot.ChunkCount);
        Assert.Equal(3, state.Chunks.Count);
        Assert.Equal(result.Snapshot, snapshot.CanonicalName);
        Assert.Equal(3, state.ChunkRepositoryInstances);
    }

    [Fact]
    public async Task Failed_Materialization_Marks_Header_Failed_And_Keeps_Chunks() {
        var state = new ReportPersistenceState();
        var driver = ReportTestHost.CreateDriver(ReportTestRows.ThrowAfter(2, "source failed"));
        using var provider = ReportTestHost.Create(driver, state, chunkSize: 2);
        var service = provider.GetRequiredService<IReportService>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            await service.RunAsync(ReportTestHost.InlineRequest(persist: true));
        });

        var snapshot = Assert.Single(state.Snapshots);
        Assert.Equal("source failed", exception.Message);
        Assert.Equal(SnapshotState.Failed, snapshot.State);
        Assert.Equal("source failed", snapshot.Error);
        Assert.Single(state.Chunks);
    }

    [Fact]
    public async Task Run_Propagates_Generate_Advisor_Rejection() {
        var driver = ReportTestHost.CreateDriver(ReportTestRows.Create(1));
        var advisor = new Mock<IReportGenerateAdvisor>();
        advisor.SetupGet(value => value.Order).Returns(0);
        advisor.Setup(value => value.AdviseAsync(
                    It.IsAny<AdviceContext>(),
                    It.IsAny<ReportGenerateContext>(),
                    It.IsAny<CancellationToken>()))
               .Throws(new PermissionDeniedException());
        using var provider = ReportTestHost.Create(
            driver,
            configure: services => services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IReportGenerateAdvisor>(advisor.Object)));
        var service = provider.GetRequiredService<IReportService>();

        await Assert.ThrowsAsync<PermissionDeniedException>(async () => {
            await service.RunAsync(ReportTestHost.InlineRequest());
        });
    }

    [Fact]
    public async Task Run_Forwards_Caller_Principal_To_Source_Driver() {
        var driver    = ReportTestHost.CreateDriver(ReportTestRows.Create(1));
        var principal = new ClaimsPrincipal(new ClaimsIdentity("test"));
        using var provider = ReportTestHost.Create(driver);
        var service = provider.GetRequiredService<IReportService>();

        await service.RunAsync(ReportTestHost.InlineRequest(), principal);

        driver.Verify(value => value.ExecuteAsync(
                          It.IsAny<SubPlan>(),
                          It.IsAny<QueryInsightRequest>(),
                          principal,
                          It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Generate_Advisor_May_Replace_The_Principal() {
        var driver      = ReportTestHost.CreateDriver(ReportTestRows.Create(1));
        var substituted = new ClaimsPrincipal(new ClaimsIdentity("service"));
        var advisor = new Mock<IReportGenerateAdvisor>();
        advisor.SetupGet(value => value.Order).Returns(0);
        advisor.Setup(value => value.AdviseAsync(
                    It.IsAny<AdviceContext>(),
                    It.IsAny<ReportGenerateContext>(),
                    It.IsAny<CancellationToken>()))
               .Callback((AdviceContext context, ReportGenerateContext reportContext, CancellationToken cancellationToken) => reportContext.Principal = substituted)
               .Returns(Task.FromResult(AdviseResult.Continue));
        using var provider = ReportTestHost.Create(
            driver,
            configure: services => services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IReportGenerateAdvisor>(advisor.Object)));
        var service = provider.GetRequiredService<IReportService>();

        await service.RunAsync(ReportTestHost.InlineRequest(), new ClaimsPrincipal(new ClaimsIdentity("caller")));

        driver.Verify(value => value.ExecuteAsync(
                          It.IsAny<SubPlan>(),
                          It.IsAny<QueryInsightRequest>(),
                          substituted,
                          It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReadRows_Streams_Chunks_In_Index_Order() {
        var state = new ReportPersistenceState();
        var driver = ReportTestHost.CreateDriver(ReportTestRows.Create(5));
        using var provider = ReportTestHost.Create(driver, state, chunkSize: 2);
        var service = provider.GetRequiredService<IReportService>();
        var store = provider.GetRequiredService<IReportSnapshotStore>();

        var result = await service.RunAsync(ReportTestHost.InlineRequest(persist: true));
        var values = new List<int>();
        await foreach (var row in store.ReadRowsAsync(result.Snapshot!)) {
            values.Add(((System.Text.Json.JsonElement)row["value"]!).GetInt32());
        }

        Assert.Equal([0, 1, 2, 3, 4], values);
    }

    [Fact]
    public async Task Run_Inline_Disposes_Source_Result() {
        var result = new Mock<ISourceResult>();
        result.SetupGet(value => value.Rows).Returns(ReportTestRows.Create(1));
        result.SetupGet(value => value.Schema).Returns([]);
        result.Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var driver = ReportTestHost.CreateDriver(result.Object);
        using var provider = ReportTestHost.Create(driver);
        var service = provider.GetRequiredService<IReportService>();

        await service.RunAsync(ReportTestHost.InlineRequest());

        result.Verify(value => value.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task Run_Persist_Disposes_Source_Result() {
        var result = new Mock<ISourceResult>();
        result.SetupGet(value => value.Rows).Returns(ReportTestRows.Create(1));
        result.SetupGet(value => value.Schema).Returns([]);
        result.Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var driver = ReportTestHost.CreateDriver(result.Object);
        using var provider = ReportTestHost.Create(driver, new ReportPersistenceState());
        var service = provider.GetRequiredService<IReportService>();

        await service.RunAsync(ReportTestHost.InlineRequest(persist: true));

        result.Verify(value => value.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task Generate_Without_Scheduler_Throws_FailedPrecondition() {
        using var provider = ReportTestHost.Create(ReportTestHost.CreateDriver(ReportTestRows.Create(1)));
        var service = provider.GetRequiredService<IReportService>();

        var exception = await Assert.ThrowsAsync<FailedPreconditionException>(async () => {
            await service.GenerateAsync(ReportTestHost.InlineRequest());
        });

        Assert.Equal("Report generation requires a scheduler.", exception.Message);
    }

    [Fact]
    public async Task Run_With_Name_And_Query_Throws_InvalidArgument() {
        using var provider = ReportTestHost.Create(ReportTestHost.CreateDriver(ReportTestRows.Create(1)));
        var service = provider.GetRequiredService<IReportService>();
        var request = ReportTestHost.InlineRequest();
        request.Name = "reports/daily";

        await Assert.ThrowsAsync<InvalidArgumentException>(async () => {
            await service.RunAsync(request);
        });
    }

    [Fact]
    public async Task Run_Unknown_Named_Report_Throws_NotFound() {
        using var provider = ReportTestHost.Create(ReportTestHost.CreateDriver(ReportTestRows.Create(1)));
        var service = provider.GetRequiredService<IReportService>();

        var exception = await Assert.ThrowsAsync<NotFoundException>(async () => {
            await service.RunAsync(ReportTestHost.NamedRequest("daily"));
        });

        Assert.Equal("Report 'daily' was not found.", exception.Message);
    }

}
