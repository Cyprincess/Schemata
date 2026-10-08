using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Tenancy;
using Schemata.Entity.Repository;
using Schemata.Event.Foundation.Runtime;
using Schemata.Flow.Event.Handlers;
using Schemata.Flow.Foundation;
using Schemata.Flow.Foundation.Builders;
using Schemata.Flow.Integration.Tests.Fixtures;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.Skeleton.Models;
using Microsoft.AspNetCore.Builder;
using Schemata.Core.Building;
using Schemata.Security.Foundation;
using Schemata.Security.Skeleton;
using Xunit;

namespace Schemata.Flow.Integration.Tests;

public sealed class ParticipantBridgeShould
{
    [Fact]
    public async Task Deliver_Trusted_Bridge_Only_In_Current_Tenant_And_Reject_Anonymous_Direct_Call() {
        var fixture = new EfCoreFlowFixture { ConfigureServices = services => {
            var flow = new SchemataFlowBuilder(new(), services);
            new SchemataResourceBuilder(flow.Schemata, services)
                .Use<SchemataProcess, SchemataProcess, SchemataProcess, SchemataProcess>()
                .Use<SchemataProcessToken, SchemataProcessToken, SchemataProcessToken, SchemataProcessToken>();
            flow.WithAuthorization();
            services.AddScoped<IPermissionResolver, DefaultPermissionResolver>();
            services.AddScoped<IPermissionMatcher, DefaultPermissionMatcher>();
            services.Configure<SchemataSecurityOptions>(options => options.PermissionClaimType = "permission");
        } };
        fixture.CatchKinds.Add(FlowCatchKind.Signal);
        await fixture.InitializeAsync();
        try {
            var tenant = Guid.NewGuid();
            var foreign = Guid.NewGuid();
            using (var scope = fixture.CreateScope()) {
                await scope.ServiceProvider.GetRequiredService<IProcessRegistry>().RegisterAsync<SecuredSignalProcess>(FlowConstants.Engines.Bpmn);
            }
            var first = await Start(tenant);
            var second = await Start(foreign);
            using (TenantContext.Enter(new(tenant))) {
                using var scope = fixture.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<FlowRunner>();
                await Assert.ThrowsAsync<PermissionDeniedException>(() => runner.ThrowSignalAsync("broadcast-signal", (string?)null, null, null, default).AsTask());
                var dispatch = new EventDispatchContext();
                dispatch.SetSubscriptions([new() { Target = first.CanonicalName!, EventType = "broadcast-signal" }]);
                await new FlowEventHandler(scope.ServiceProvider, dispatch).HandleAsync(new ApprovalPayload { Approved = true }, default);
                var row = await scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcessToken>>().FirstOrDefaultAsync(q => q.Where(t => t.Process == first.Name));
                Assert.Null(row!.WaitingAtName);
                var principal = Principal("signal", "administer");
                var results = await runner.ThrowSignalAsync("broadcast-signal", (string?)null, null, principal, default);
                Assert.DoesNotContain(results, result => result.ProcessCanonicalName == second.CanonicalName);
            }
            using (TenantContext.Enter(new(foreign))) {
                using var scope = fixture.CreateScope();
                var row = await scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcessToken>>().FirstOrDefaultAsync(q => q.Where(t => t.Process == second.Name));
                Assert.Equal("signal-catch", row!.WaitingAtName);
            }
            async Task<SchemataProcess> Start(Guid id) {
                using var entered = TenantContext.Enter(new(id));
                using var scope = fixture.CreateScope();
                return await scope.ServiceProvider.GetRequiredService<FlowRunner>().StartAsync(nameof(SecuredSignalProcess), null, Principal("start"), CancellationToken.None);
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
        new[] { new Claim("sub", "users/actor") }.Concat(operations.Select(operation => new Claim("permission", "schemata-process." + operation))), "test"));
}
