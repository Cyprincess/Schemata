using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Messaging.Skeleton.Commands;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Foundation.Commands;
using Schemata.Scheduling.Foundation.Runtime;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Security.Foundation;
using Schemata.Security.Skeleton;
using Xunit;

namespace Schemata.Scheduling.Tests;

/// <summary>
///     Proves the AIP-136 verb envelope reaches Scheduling's original trigger handler through the
///     dispatcher and exposes (verb, name, entity) to wrap-position advisors on both entry styles:
///     the <see cref="IScheduler.TriggerAsync{TJob}" /> facade and a direct envelope dispatch.
/// </summary>
public sealed class SchedulingMethodEnvelopeShould
{
    [Fact]
    public async Task Trigger_Facade_Wraps_The_Verb_Envelope_With_The_Job_Name() {
        var wrap    = new RecordingEnvelopeAdvisor();
        var command = new RecordingCommandAdvisor();
        var harness = await CreateStartedHarnessAsync(services => {
            services.AddSingleton<IRequestPipelineAdvisor<ResourceMethodRequest<SchemataJob, TriggerJobRequest, SchemataJobExecution>, SchemataJobExecution>>(wrap);
            services.AddSingleton<IRequestPipelineAdvisor<TriggerJobRequest, SchemataJobExecution>>(command);
        });

        var execution = await harness.Scheduler.TriggerAsync<SampleJob>(
            new() { Job = "sample" }, CancellationToken.None);
        Assert.Equal("sample", execution.Job);

        var observed = Assert.Single(wrap.Observed);
        Assert.Equal(SchedulingOperations.Trigger, observed.Verb);
        Assert.Equal("sample", observed.Name);
        Assert.Equal(typeof(SchemataJob), observed.Entity);
        Assert.Equal(1, command.Count);
    }

    [Fact]
    public async Task Trigger_Facade_Carries_The_Context_Principal_Through_The_Envelope() {
        var wrap    = new RecordingEnvelopeAdvisor();
        var command = new RecordingCommandAdvisor();
        var harness = await CreateStartedHarnessAsync(services => {
            services.AddSingleton<IRequestPipelineAdvisor<ResourceMethodRequest<SchemataJob, TriggerJobRequest, SchemataJobExecution>, SchemataJobExecution>>(wrap);
            services.AddSingleton<IRequestPipelineAdvisor<TriggerJobRequest, SchemataJobExecution>>(command);
        });
        var principal = new ClaimsPrincipal(new ClaimsIdentity("test"));

        var execution = await harness.Scheduler.TriggerAsync<SampleJob>(
            new() { Job = "sample", Principal = principal }, CancellationToken.None);

        Assert.Equal("sample", execution.Job);
        Assert.Same(principal, Assert.Single(wrap.Observed).Principal);
        Assert.Same(principal, command.Principal);
    }


    [Fact]
    public async Task Authorization_Only_Denies_And_Matching_Permission_Allows_Trigger() {
        var denied = await CreateStartedHarnessAsync(services => {
            services.Configure<SchemataSecurityOptions>(_ => { });
            services.AddScoped<IPermissionResolver, DefaultPermissionResolver>();
            services.AddScoped<IPermissionMatcher, DefaultPermissionMatcher>();
            services.AddSchedulingAuthorization();
        });
        var deniedDispatcher = denied.Services.GetRequiredService<IRequestDispatcher>();
        var principal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity("test"));

        await Assert.ThrowsAsync<PermissionDeniedException>(() => deniedDispatcher.SendAsync<ResourceMethodRequest<SchemataJob, TriggerJobRequest, SchemataJobExecution>, SchemataJobExecution>(
            new(SchedulingOperations.Trigger, "sample", new("sample", typeof(SampleJob), new() { Job = "sample" }), principal), CancellationToken.None));

        var allowed = await CreateStartedHarnessAsync(services => {
            services.Configure<SchemataSecurityOptions>(_ => { });
            services.AddScoped<IPermissionResolver, DefaultPermissionResolver>();
            services.AddScoped<IPermissionMatcher, DefaultPermissionMatcher>();
            services.AddSchedulingAuthorization();
        });
        var allowedPrincipal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([new("role", "schemata-job.run")], "test"));

        var execution = await allowed.Services.GetRequiredService<IRequestDispatcher>().SendAsync<ResourceMethodRequest<SchemataJob, TriggerJobRequest, SchemataJobExecution>, SchemataJobExecution>(
            new(SchedulingOperations.Trigger, "sample", new("sample", typeof(SampleJob), new() { Job = "sample" }), allowedPrincipal), CancellationToken.None);

        Assert.Equal("sample", execution.Job);
    }

    [Fact]
    public async Task Authorization_Admits_The_System_Principal_Without_Permission_Claims() {
        var harness = await CreateStartedHarnessAsync(services => {
            services.Configure<SchemataSecurityOptions>(_ => { });
            services.AddScoped<IPermissionResolver, DefaultPermissionResolver>();
            services.AddScoped<IPermissionMatcher, DefaultPermissionMatcher>();
            services.AddSchedulingAuthorization();
        });

        var execution = await harness.Services.GetRequiredService<IRequestDispatcher>().SendAsync<ResourceMethodRequest<SchemataJob, TriggerJobRequest, SchemataJobExecution>, SchemataJobExecution>(
            new(SchedulingOperations.Trigger, "sample", new("sample", typeof(SampleJob), new() { Job = "sample" }), SchedulingSystemPrincipal.Instance), CancellationToken.None);

        Assert.Equal("sample", execution.Job);
    }

    [Fact]
    public async Task Combined_Security_Rejects_Unauthenticated_Trigger() {
        var harness = await CreateStartedHarnessAsync(services => {
            services.Configure<SchemataSecurityOptions>(_ => { });
            services.AddScoped<IPermissionResolver, DefaultPermissionResolver>();
            services.AddScoped<IPermissionMatcher, DefaultPermissionMatcher>();
            services.AddSchedulingAuthentication();
            services.AddSchedulingAuthorization();
        });

        await Assert.ThrowsAsync<UnauthenticatedException>(() => harness.Services.GetRequiredService<IRequestDispatcher>()
            .SendAsync<ResourceMethodRequest<SchemataJob, TriggerJobRequest, SchemataJobExecution>, SchemataJobExecution>(
                new(SchedulingOperations.Trigger, "sample", new("sample", typeof(SampleJob), new() { Job = "sample" }), null), CancellationToken.None));
    }

    private static async Task<Harness> CreateStartedHarnessAsync(Action<IServiceCollection>? advisors = null) {
        var harness = new Harness();

        var registry = new Mock<IScheduledJobRegistry>();
        registry.Setup(r => r.ResolveKey(typeof(SampleJob))).Returns("sample-key");

        var jobs = new Mock<IRepository<SchemataJob>>();
        jobs.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<SchemataJob?>((SchemataJob?)null));
        jobs.Setup(r => r.AddAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>())).ReturnsAsync(MutationResult.Applied);
        jobs.Setup(r => r.UpdateAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>())).ReturnsAsync(MutationResult.Applied);
        jobs.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        jobs.Setup(r => r.Begin()).Returns(CommittingUnit);

        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.ListAsync(It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(), It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> _, CancellationToken _) => Empty());
        executions.Setup(r => r.AddAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>())).ReturnsAsync(MutationResult.Applied);
        executions.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>())).ReturnsAsync(MutationResult.Applied);
        executions.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        executions.Setup(r => r.Begin()).Returns(CommittingUnit);

        var collection = new ServiceCollection()
                        .AddScoped(typeof(IResourceMutation<>), typeof(ResourceMutation<>))
                        .AddSingleton(registry.Object)
                        .AddSingleton(jobs.Object)
                        .AddSingleton(executions.Object)
                        .AddSingleton<IOptions<SchemataSchedulingOptions>>(Options.Create(new SchemataSchedulingOptions()));
        advisors?.Invoke(collection);
        collection.AddSchemataScheduling();
        harness.Services = collection.BuildServiceProvider();
        harness.Scheduler = harness.Services.GetRequiredService<DefaultScheduler>();

        await harness.Scheduler.StartAsync(CancellationToken.None);
        return harness;
    }

    private static async IAsyncEnumerable<SchemataJobExecution> Empty() {
        await Task.CompletedTask;
        yield break;
    }

    private static IUnitOfWork CommittingUnit() {
        var unit = new Mock<IUnitOfWork>(MockBehavior.Strict);
        unit.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        unit.Setup(u => u.DisposeAsync()).Returns(ValueTask.CompletedTask);
        return unit.Object;
    }

    private sealed class RecordingEnvelopeAdvisor : IRequestPipelineAdvisor<ResourceMethodRequest<SchemataJob, TriggerJobRequest, SchemataJobExecution>, SchemataJobExecution>
    {
        public List<(string Verb, string? Name, Type Entity, ClaimsPrincipal? Principal)> Observed { get; } = [];

        public int Order => 0;

        public Task<SchemataJobExecution> AdviseAsync(
            AdviceContext                                                                  ctx,
            ResourceMethodRequest<SchemataJob, TriggerJobRequest, SchemataJobExecution>    request,
            RequestHandlerContinuation<SchemataJobExecution>                               next,
            CancellationToken                                                              ct = default
        ) {
            Observed.Add((request.Verb, request.Name, request.GetType().GetGenericArguments()[0], request.Principal));
            return next(ct);
        }
    }

    private sealed class RecordingCommandAdvisor : IRequestPipelineAdvisor<TriggerJobRequest, SchemataJobExecution>
    {
        public int Count { get; private set; }

        public ClaimsPrincipal? Principal { get; private set; }

        public int Order => 0;

        public Task<SchemataJobExecution> AdviseAsync(
            AdviceContext                                   ctx,
            TriggerJobRequest                               request,
            RequestHandlerContinuation<SchemataJobExecution> next,
            CancellationToken                               ct = default) {
            Count++;
            Principal = request.Principal;
            return next(ct);
        }
    }

    private sealed class Harness
    {
        public DefaultScheduler Scheduler { get; set; } = null!;

        public ServiceProvider Services { get; set; } = null!;
    }

    private sealed class SampleJob : IScheduledJob
    {
        public Task ExecuteAsync(JobContext context, CancellationToken ct) { return Task.CompletedTask; }
    }
}
