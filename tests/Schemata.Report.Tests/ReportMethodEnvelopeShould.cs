using Schemata.Report.Tests.Fixtures;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Resource;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Messaging.Skeleton.Commands;
using Schemata.Report.Foundation;
using Schemata.Report.Foundation.Commands;
using Schemata.Report.Skeleton;
using Schemata.Scheduling.Skeleton;
using Schemata.Abstractions.Exceptions;
using Schemata.Security.Foundation;
using Schemata.Security.Skeleton;
using Xunit;

using Schemata.Report.Skeleton.Models;
using Schemata.Report.Skeleton.Entities;

namespace Schemata.Report.Tests;

/// <summary>
///     Proves the AIP-136 verb envelope reaches Report's original command handlers through the
///     dispatcher and exposes (verb, name, entity) to wrap-position advisors, with the caller's
///     principal forwarded onto the wire command.
/// </summary>
public class ReportMethodEnvelopeShould
{
    [Fact]
    public async Task Run_Envelope_Dispatch_Runs_The_Report_Handler_And_Exposes_The_Verb_To_Wraps() {
        var wrap    = new RecordingRunEnvelopeAdvisor();
        var command = new RecordingRunCommandAdvisor();
        using var provider = ReportTestHost.Create(
            ReportTestHost.CreateDriver(ReportTestRows.Create(1)),
            configure: services => {
                services.AddSingleton<IRequestPipelineAdvisor<ResourceMethodRequest<SchemataReport, RunReportRequest, ReportResult>, ReportResult>>(wrap);
                services.AddSingleton<IRequestPipelineAdvisor<RunReportRequest, ReportResult>>(command);
            });
        var dispatcher = provider.GetRequiredService<IRequestDispatcher>();
        var principal  = new ClaimsPrincipal(new ClaimsIdentity("test"));

        var result = await dispatcher.SendAsync<ResourceMethodRequest<SchemataReport, RunReportRequest, ReportResult>, ReportResult>(
            new(ReportOperations.Run, null, new(ReportTestHost.InlineRequest(), principal), principal),
            CancellationToken.None);

        var observed = Assert.Single(wrap.Observed);
        Assert.Equal(ReportOperations.Run, observed.Verb);
        Assert.Null(observed.Name);
        Assert.Equal(typeof(SchemataReport), observed.Entity);
        Assert.Single(result.Response.Rows);
        Assert.Equal(1, command.Count);
    }

    [Fact]
    public async Task Run_Facade_Wraps_The_Verb_Envelope_And_Forwards_The_Principal() {
        var wrap    = new RecordingRunEnvelopeAdvisor();
        var command = new RecordingRunCommandAdvisor();
        using var provider = ReportTestHost.Create(
            ReportTestHost.CreateDriver(ReportTestRows.Create(1)),
            configure: services => {
                services.AddSingleton<IRequestPipelineAdvisor<ResourceMethodRequest<SchemataReport, RunReportRequest, ReportResult>, ReportResult>>(wrap);
                services.AddSingleton<IRequestPipelineAdvisor<RunReportRequest, ReportResult>>(command);
            });
        var principal = new ClaimsPrincipal(new ClaimsIdentity("test"));

        var result = await provider.GetRequiredService<IReportService>()
                                   .RunAsync(ReportTestHost.InlineRequest(), principal);

        var observed = Assert.Single(wrap.Observed);
        Assert.Equal(ReportOperations.Run, observed.Verb);
        Assert.Equal(typeof(SchemataReport), observed.Entity);
        Assert.Same(principal, wrap.Principal);
        Assert.Same(principal, command.Principal);
        Assert.Single(result.Response.Rows);
    }

    [Fact]
    public async Task Generate_Envelope_Forwards_The_Principal_Onto_The_Wire_Command() {
        var wrap = new RecordingGenerateEnvelopeAdvisor();
        ClaimsPrincipal? forwarded = null;
        var command = new Mock<IRequestPipelineAdvisor<GenerateReportRequest, Operation>>();
        command.SetupGet(value => value.Order).Returns(0);
        command.Setup(value => value.AdviseAsync(
                          It.IsAny<AdviceContext>(),
                          It.IsAny<GenerateReportRequest>(),
                          It.IsAny<RequestHandlerContinuation<Operation>>(),
                          It.IsAny<CancellationToken>()))
                      .Callback((AdviceContext _, GenerateReportRequest request, RequestHandlerContinuation<Operation> _, CancellationToken _) => forwarded = request.Principal)
                      .Returns((AdviceContext _, GenerateReportRequest _, RequestHandlerContinuation<Operation> _, CancellationToken _) => Task.FromResult(new Operation { Done = true }));
        using var provider = ReportTestHost.Create(
            ReportTestHost.CreateDriver(ReportTestRows.Create(1)),
            configure: services => {
                services.AddSingleton<IRequestPipelineAdvisor<ResourceMethodRequest<SchemataReport, GenerateReportRequest, Operation>, Operation>>(wrap);
                services.AddSingleton(command.Object);
                services.AddSingleton(new Mock<IOperationService>(MockBehavior.Loose).Object);
            });
        var dispatcher = provider.GetRequiredService<IRequestDispatcher>();
        var principal  = new ClaimsPrincipal(new ClaimsIdentity("test"));

        await dispatcher.SendAsync<ResourceMethodRequest<SchemataReport, GenerateReportRequest, Operation>, Operation>(
            new(ReportOperations.Generate, null, new GenerateReportRequest { Query = ReportTestHost.InlineRequest().Query, Sync = true }, principal),
            CancellationToken.None);

        var observed = Assert.Single(wrap.Observed);
        Assert.Equal(ReportOperations.Generate, observed.Verb);
        Assert.Equal(typeof(SchemataReport), observed.Entity);
        Assert.Same(principal, forwarded);
    }

    [Fact]
    public async Task Authorization_Only_Denies_And_Matching_Permission_Allows_Run() {
        using var denied = ReportTestHost.Create(ReportTestHost.CreateDriver(ReportTestRows.Create(1)), configure: services => {
            services.Configure<SchemataSecurityOptions>(_ => { });
            services.AddScoped<IPermissionResolver, DefaultPermissionResolver>();
            services.AddScoped<IPermissionMatcher, DefaultPermissionMatcher>();
            services.AddReportAuthorization<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>();
        });
        var deniedRequest = new ResourceMethodRequest<SchemataReport, RunReportRequest, ReportResult>(
            ReportOperations.Run, null, new(ReportTestHost.InlineRequest(), new(new ClaimsIdentity("test"))), new(new ClaimsIdentity("test")));

        await Assert.ThrowsAsync<PermissionDeniedException>(() => denied.GetRequiredService<IRequestDispatcher>()
            .SendAsync<ResourceMethodRequest<SchemataReport, RunReportRequest, ReportResult>, ReportResult>(deniedRequest));

        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("role", "schemata-report.run")], "test"));
        using var allowed = ReportTestHost.Create(ReportTestHost.CreateDriver(ReportTestRows.Create(1)), configure: services => {
            services.Configure<SchemataSecurityOptions>(_ => { });
            services.AddScoped<IPermissionResolver, DefaultPermissionResolver>();
            services.AddScoped<IPermissionMatcher, DefaultPermissionMatcher>();
            services.AddReportAuthorization<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>();
        });

        var result = await allowed.GetRequiredService<IRequestDispatcher>()
            .SendAsync<ResourceMethodRequest<SchemataReport, RunReportRequest, ReportResult>, ReportResult>(
                new(ReportOperations.Run, null, new(ReportTestHost.InlineRequest(), principal), principal));

        Assert.Single(result.Response.Rows);
    }

    [Fact]
    public async Task Combined_Security_Rejects_Unauthenticated_Run() {
        using var provider = ReportTestHost.Create(ReportTestHost.CreateDriver(ReportTestRows.Create(1)), configure: services => {
            services.Configure<SchemataSecurityOptions>(_ => { });
            services.AddScoped<IPermissionResolver, DefaultPermissionResolver>();
            services.AddScoped<IPermissionMatcher, DefaultPermissionMatcher>();
            services.AddReportAuthentication<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>();
            services.AddReportAuthorization<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>();
        });

        await Assert.ThrowsAsync<UnauthenticatedException>(() => provider.GetRequiredService<IRequestDispatcher>()
            .SendAsync<ResourceMethodRequest<SchemataReport, RunReportRequest, ReportResult>, ReportResult>(
                new(ReportOperations.Run, null, new(ReportTestHost.InlineRequest(), null), null)));
    }

    private sealed class RecordingRunEnvelopeAdvisor : IRequestPipelineAdvisor<ResourceMethodRequest<SchemataReport, RunReportRequest, ReportResult>, ReportResult>
    {
        public List<(string Verb, string? Name, Type Entity)> Observed { get; } = [];

        public ClaimsPrincipal? Principal { get; private set; }

        public int Order => 0;

        public Task<ReportResult> AdviseAsync(
            AdviceContext                                                         ctx,
            ResourceMethodRequest<SchemataReport, RunReportRequest, ReportResult> request,
            RequestHandlerContinuation<ReportResult>                              next,
            CancellationToken                                                     ct = default
        ) {
            Observed.Add((request.Verb, request.Name, request.GetType().GetGenericArguments()[0]));
            Principal = request.Principal;
            return next(ct);
        }
    }

    private sealed class RecordingRunCommandAdvisor : IRequestPipelineAdvisor<RunReportRequest, ReportResult>
    {
        public int Count { get; private set; }

        public ClaimsPrincipal? Principal { get; private set; }

        public int Order => 0;

        public Task<ReportResult> AdviseAsync(
            AdviceContext                            ctx,
            RunReportRequest                         request,
            RequestHandlerContinuation<ReportResult> next,
            CancellationToken                        ct = default) {
            Count++;
            Principal = request.Principal;
            return next(ct);
        }
    }

    private sealed class RecordingGenerateEnvelopeAdvisor : IRequestPipelineAdvisor<ResourceMethodRequest<SchemataReport, GenerateReportRequest, Operation>, Operation>
    {
        public List<(string Verb, string? Name, Type Entity)> Observed { get; } = [];

        public int Order => 0;

        public Task<Operation> AdviseAsync(
            AdviceContext                                                              ctx,
            ResourceMethodRequest<SchemataReport, GenerateReportRequest, Operation>    request,
            RequestHandlerContinuation<Operation>                                      next,
            CancellationToken                                                          ct = default
        ) {
            Observed.Add((request.Verb, request.Name, request.GetType().GetGenericArguments()[0]));
            return next(ct);
        }
    }
}
