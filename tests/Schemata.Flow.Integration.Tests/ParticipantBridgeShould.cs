using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Event.Foundation.Runtime;
using Schemata.Flow.Event.Handlers;
using Schemata.Flow.Foundation;
using Schemata.Flow.Integration.Tests.Fixtures;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Observers;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.Skeleton.Models;
using Microsoft.AspNetCore.Builder;
using Moq;
using Schemata.Core.Building;
using Schemata.Security.Foundation;
using Schemata.Security.Skeleton;
using Xunit;

namespace Schemata.Flow.Integration.Tests;

public sealed class ParticipantBridgeShould
{
    [Fact]
    public async Task Deliver_Trusted_Bridge_Only_In_Current_Tenant_And_Reject_Anonymous_Direct_Call() {
        var fixture = new TenantFlowFixture(false) { ConfigureServices = services => {
            var flow = new Schemata.Flow.Foundation.Builders.SchemataFlowBuilder<TenantProcess, TenantProcessToken, TenantProcessTransition,
                TenantProcessCompensation, TenantProcessParticipant, TenantProcessSource, TenantFlowEffectIntent>(new(), services);
            new SchemataResourceBuilder(flow.Schemata, services)
                .Use<TenantProcess, TenantProcess, TenantProcess, TenantProcess>()
                .Use<TenantProcessToken, TenantProcessToken, TenantProcessToken, TenantProcessToken>();
            flow.WithAuthorization();
            services.AddScoped<IPermissionResolver, DefaultPermissionResolver>();
            services.AddScoped<IPermissionMatcher, DefaultPermissionMatcher>();
            services.Configure<SchemataSecurityOptions>(options => options.PermissionClaimType = "permission");
            var catches = new Mock<IFlowCatchHandler>();
            catches.Setup(handler => handler.Handles(It.IsAny<FlowCatchKind>()))
                   .Returns<FlowCatchKind>(kind => kind is FlowCatchKind.Signal);
            catches.Setup(handler => handler.ArmAsync(It.IsAny<FlowTransitionContext>(), It.IsAny<CancellationToken>()))
                   .Returns(ValueTask.CompletedTask);
            services.AddSingleton(catches.Object);
        } };
        await fixture.InitializeAsync();
        try {
            var tenant = Guid.NewGuid();
            var foreign = Guid.NewGuid();
            using (var scope = fixture.CreateScope()) {
                await scope.ServiceProvider.GetRequiredService<IProcessRegistry>().RegisterAsync<SecuredSignalProcess>(FlowConstants.Engines.Bpmn);
            }
            var first = await Start(tenant);
            var second = await Start(foreign);
            await using (var scope = await fixture.EnterTenantAsync(tenant)) {
                var runner = scope.ServiceProvider.GetRequiredService<FlowRunner<TenantProcess, TenantProcessToken>>();
                await Assert.ThrowsAsync<PermissionDeniedException>(() => runner.ThrowSignalAsync("broadcast-signal", (string?)null, null, null, default).AsTask());
                var dispatch = new EventDispatchContext();
                dispatch.SetSubscriptions([new() { Target = first.CanonicalName!, EventType = "broadcast-signal" }]);
                await new FlowEventHandler(scope.ServiceProvider, dispatch).HandleAsync(new ApprovalPayload { Approved = true }, default);
                var row = await scope.ServiceProvider.GetRequiredService<IRepository<TenantProcessToken>>().FirstOrDefaultAsync(q => q.Where(t => t.Process == first.Name));
                Assert.Null(row!.WaitingAtName);
                var principal = Principal("signal", "administer");
                var results = await runner.ThrowSignalAsync("broadcast-signal", (string?)null, null, principal, default);
                Assert.DoesNotContain(results, result => result.ProcessCanonicalName == second.CanonicalName);
            }
            await using (var scope = await fixture.EnterTenantAsync(foreign)) {
                var row = await scope.ServiceProvider.GetRequiredService<IRepository<TenantProcessToken>>().FirstOrDefaultAsync(q => q.Where(t => t.Process == second.Name));
                Assert.Equal("signal-catch", row!.WaitingAtName);
            }
            async Task<TenantProcess> Start(Guid id) {
                await using var scope = await fixture.EnterTenantAsync(id);
                return await scope.ServiceProvider.GetRequiredService<FlowRunner<TenantProcess, TenantProcessToken>>()
                                    .StartAsync(nameof(SecuredSignalProcess), null, Principal("start"), CancellationToken.None);
            }
        } finally { await fixture.DisposeAsync(); }
    }

    public sealed class SecuredSignalProcess : ProcessDefinition
    {
        public SecuredSignalProcess() {
            var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
            var signal = new Signal<ApprovalPayload> { Name = "broadcast-signal" };
            var wait = new FlowEvent { Name = "signal-catch", Position = EventPosition.IntermediateCatch, Definition = signal };
            var end = new FlowEvent { Name = "end", Position = EventPosition.End };
            Elements.AddRange([start, wait, end]);
            Signals.Add(signal);
            Flows.Add(new() { Source = start, Target = wait });
            Flows.Add(new() { Source = wait, Target = end });
        }
    }

    private static ClaimsPrincipal Principal(params string[] operations) => new(new ClaimsIdentity(
        new[] { new Claim("sub", "users/actor") }.Concat(operations.Select(operation => new Claim("permission", "tenant-process." + operation))), "test"));
}
