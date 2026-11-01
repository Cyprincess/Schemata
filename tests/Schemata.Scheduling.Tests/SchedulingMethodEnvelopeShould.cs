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
using static Schemata.Abstractions.SchemataConstants;
using Operation = Schemata.Abstractions.Resource.Operation;

namespace Schemata.Scheduling.Tests;

/// <summary>
///     Proves scheduler-initiated fires enter the base command lifecycle without a caller or a
///     verb envelope, while the AIP-136 <c>:run</c> envelope admits its caller through the
///     authentication and authorization stages before reaching the same lifecycle.
/// </summary>
public sealed class SchedulingMethodEnvelopeShould
{
    [Fact]
    public async Task Trigger_Facade_Dispatches_The_Command_Directly_Without_The_Verb_Envelope() {
        var wrap    = new RecordingRunEnvelopeAdvisor();
        var stale   = new RecordingTriggerEnvelopeAdvisor();
        var command = new RecordingCommandAdvisor();
        var harness = await CreateStartedHarnessAsync(services => {
            services.AddSingleton<IRequestPipelineAdvisor<ResourceMethodRequest<SchemataJob, RunJobRequest, Operation>, Operation>>(wrap);
            services.AddSingleton<IRequestPipelineAdvisor<ResourceMethodRequest<SchemataJob, TriggerJobRequest<SchemataJobExecution>, SchemataJobExecution>, SchemataJobExecution>>(stale);
            services.AddSingleton<IRequestPipelineAdvisor<TriggerJobRequest<SchemataJobExecution>, SchemataJobExecution>>(command);
        });

        var execution = await harness.Scheduler.TriggerAsync<SampleJob>(
            new() { Job = "sample" }, CancellationToken.None);

        Assert.Equal("sample", execution.Job);
        Assert.Equal(1, command.Count);
        Assert.Empty(wrap.Observed);
        Assert.Empty(stale.Observed);
    }

    [Fact]
    public async Task Automatic_Trigger_Advances_Without_A_Caller_Under_Enabled_Authorization() {
        var harness = await CreateStartedHarnessAsync(services => {
            services.Configure<SchemataSecurityOptions>(_ => { });
            services.AddScoped<IPermissionResolver, DefaultPermissionResolver>();
            services.AddScoped<IPermissionMatcher, DefaultPermissionMatcher>();
            SchedulingAuthorizationRegistration<SchemataJob, SchemataJobExecution>.AddSchedulingAuthentication(services);
            SchedulingAuthorizationRegistration<SchemataJob, SchemataJobExecution>.AddSchedulingAuthorization(services);
        });

        var execution = await harness.Scheduler.TriggerAsync<SampleJob>(
            new() { Job = "sample" }, CancellationToken.None);

        Assert.Equal("sample", execution.Job);
        Assert.Equal(ExecutionState.Pending, execution.State);
    }

    [Fact]
    public async Task Run_Envelope_Persists_The_Execution_For_A_Permitted_Caller() {
        var harness = await CreateStartedHarnessAsync(services => {
            services.Configure<SchemataSecurityOptions>(_ => { });
            services.AddScoped<IPermissionResolver, DefaultPermissionResolver>();
            services.AddScoped<IPermissionMatcher, DefaultPermissionMatcher>();
            SchedulingAuthorizationRegistration<SchemataJob, SchemataJobExecution>.AddSchedulingAuthentication(services);
            SchedulingAuthorizationRegistration<SchemataJob, SchemataJobExecution>.AddSchedulingAuthorization(services);
        }, PersistedJob());
        var dispatcher = harness.Services.GetRequiredService<IRequestDispatcher>();
        var caller = new ClaimsPrincipal(new ClaimsIdentity([new Claim("role", "schemata-job.run")], "test"));

        var operation = await dispatcher.SendAsync<ResourceMethodRequest<SchemataJob, RunJobRequest, Operation>, Operation>(
            new(Verbs.Run, "jobs/sample", new() { CanonicalName = "jobs/sample" }, caller), CancellationToken.None);

        Assert.Equal("jobs/sample", operation.Metadata?.Job);
        Assert.False(operation.Done);
    }

    [Fact]
    public async Task Run_Envelope_Denies_A_Caller_Without_The_Run_Permission() {
        var harness = await CreateStartedHarnessAsync(services => {
            services.Configure<SchemataSecurityOptions>(_ => { });
            services.AddScoped<IPermissionResolver, DefaultPermissionResolver>();
            services.AddScoped<IPermissionMatcher, DefaultPermissionMatcher>();
            SchedulingAuthorizationRegistration<SchemataJob, SchemataJobExecution>.AddSchedulingAuthentication(services);
            SchedulingAuthorizationRegistration<SchemataJob, SchemataJobExecution>.AddSchedulingAuthorization(services);
        }, PersistedJob());
        var dispatcher = harness.Services.GetRequiredService<IRequestDispatcher>();
        var caller = new ClaimsPrincipal(new ClaimsIdentity("test"));

        await Assert.ThrowsAsync<PermissionDeniedException>(() => dispatcher.SendAsync<ResourceMethodRequest<SchemataJob, RunJobRequest, Operation>, Operation>(
            new(Verbs.Run, "jobs/sample", new() { CanonicalName = "jobs/sample" }, caller), CancellationToken.None));
    }

    [Fact]
    public async Task Run_Envelope_Rejects_A_Null_Caller() {
        var harness = await CreateStartedHarnessAsync(services => {
            services.Configure<SchemataSecurityOptions>(_ => { });
            services.AddScoped<IPermissionResolver, DefaultPermissionResolver>();
            services.AddScoped<IPermissionMatcher, DefaultPermissionMatcher>();
            SchedulingAuthorizationRegistration<SchemataJob, SchemataJobExecution>.AddSchedulingAuthentication(services);
            SchedulingAuthorizationRegistration<SchemataJob, SchemataJobExecution>.AddSchedulingAuthorization(services);
        }, PersistedJob());
        var dispatcher = harness.Services.GetRequiredService<IRequestDispatcher>();

        await Assert.ThrowsAsync<UnauthenticatedException>(() => dispatcher.SendAsync<ResourceMethodRequest<SchemataJob, RunJobRequest, Operation>, Operation>(
            new(Verbs.Run, "jobs/sample", new() { CanonicalName = "jobs/sample" }, null), CancellationToken.None));
    }

    private static SchemataJob PersistedJob() {
        return new() { CanonicalName = "jobs/sample", JobKey = "sample-key" };
    }

    private static async Task<Harness> CreateStartedHarnessAsync(Action<IServiceCollection>? advisors = null, SchemataJob? persisted = null) {
        var harness = new Harness();

        var registry = new Mock<IScheduledJobRegistry>();
        registry.Setup(r => r.ResolveKey(typeof(SampleJob))).Returns("sample-key");
        registry.Setup(r => r.Resolve("sample-key")).Returns(typeof(SampleJob));

        var jobs = new Mock<IRepository<SchemataJob>>();
        jobs.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(), It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
            .Returns(new ValueTask<SchemataJob?>(persisted));
        jobs.Setup(r => r.AddAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>())).ReturnsAsync(MutationResult.Applied);
        jobs.Setup(r => r.UpdateAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>())).ReturnsAsync(MutationResult.Applied);
        jobs.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        jobs.Setup(r => r.Begin()).Returns(CommittingUnit);

        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.ListAsync(It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(), It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                  .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> _, CancellationToken _, QueryOptions _) => Empty());
        executions.Setup(r => r.AddAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>())).ReturnsAsync(MutationResult.Applied);
        executions.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>())).ReturnsAsync(MutationResult.Applied);
        executions.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        executions.Setup(r => r.Begin()).Returns(CommittingUnit);

        var collection = new ServiceCollection()
                        .AddScoped(typeof(IResourceMutation<>), typeof(ResourceMutation<>))
                        .AddSingleton(registry.Object)
                        .AddSingleton(jobs.Object)
                        .AddSingleton(executions.Object)
                        .AddSingleton<IOptions<SchemataSchedulingOptions>>(Options.Create(new SchemataSchedulingOptions()))
                        .AddTransient<SampleJob>()
                        .AddScoped<IRequestHandler<RunJobRequest, Operation>, RunJobHandler<SchemataJob>>()
                        .AddTransient<IRequestHandler<ResourceMethodRequest<SchemataJob, RunJobRequest, Operation>, Operation>, ResourceMethodForwardHandler<SchemataJob, RunJobRequest, Operation>>();
        advisors?.Invoke(collection);
        collection.AddSchemataScheduling();
        harness.Services = collection.BuildServiceProvider();
        harness.Scheduler = harness.Services.GetRequiredService<DefaultScheduler<SchemataJob, SchemataJobExecution>>();

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

    private sealed class RecordingRunEnvelopeAdvisor : IRequestPipelineAdvisor<ResourceMethodRequest<SchemataJob, RunJobRequest, Operation>, Operation>
    {
        public List<(string Verb, string? Name, Type Entity)> Observed { get; } = [];

        public int Order => 0;

        public Task<Operation> AdviseAsync(
            AdviceContext                                              ctx,
            ResourceMethodRequest<SchemataJob, RunJobRequest, Operation> request,
            RequestHandlerContinuation<Operation>                      next,
            CancellationToken                                          ct = default
        ) {
            Observed.Add((request.Verb, request.Name, request.GetType().GetGenericArguments()[0]));
            return next(ct);
        }
    }

    private sealed class RecordingTriggerEnvelopeAdvisor : IRequestPipelineAdvisor<ResourceMethodRequest<SchemataJob, TriggerJobRequest<SchemataJobExecution>, SchemataJobExecution>, SchemataJobExecution>
    {
        public List<(string Verb, string? Name, Type Entity)> Observed { get; } = [];

        public int Order => 0;

        public Task<SchemataJobExecution> AdviseAsync(
            AdviceContext                                                                  ctx,
            ResourceMethodRequest<SchemataJob, TriggerJobRequest<SchemataJobExecution>, SchemataJobExecution>    request,
            RequestHandlerContinuation<SchemataJobExecution>                               next,
            CancellationToken                                                              ct = default
        ) {
            Observed.Add((request.Verb, request.Name, request.GetType().GetGenericArguments()[0]));
            return next(ct);
        }
    }

    private sealed class RecordingCommandAdvisor : IRequestPipelineAdvisor<TriggerJobRequest<SchemataJobExecution>, SchemataJobExecution>
    {
        public int Count { get; private set; }

        public int Order => 0;

        public Task<SchemataJobExecution> AdviseAsync(
            AdviceContext                                   ctx,
            TriggerJobRequest<SchemataJobExecution>                               request,
            RequestHandlerContinuation<SchemataJobExecution> next,
            CancellationToken                               ct = default) {
            Count++;
            return next(ct);
        }
    }

    private sealed class Harness
    {
        public DefaultScheduler<SchemataJob, SchemataJobExecution> Scheduler { get; set; } = null!;

        public ServiceProvider Services { get; set; } = null!;
    }

    private sealed class SampleJob : IScheduledJob
    {
        public Task ExecuteAsync(JobContext context, CancellationToken ct) { return Task.CompletedTask; }
    }
}
