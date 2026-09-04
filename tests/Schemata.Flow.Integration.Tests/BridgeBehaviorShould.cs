using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.Repository;
using Schemata.Event.Foundation.Runtime;
using Schemata.Flow.Event.Handlers;
using Schemata.Flow.Foundation;
using Schemata.Flow.Integration.Tests.Fixtures;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class BridgeBehaviorShould : IClassFixture<EfCoreFlowFixture>
{
    private readonly EfCoreFlowFixture _fixture;

    public BridgeBehaviorShould(EfCoreFlowFixture fixture) { _fixture = fixture; }

    [Fact]
    public async Task Forward_Typed_Bus_Payload_To_The_Addressed_Message_Token() {
        _fixture.CatchKinds.Add(FlowCatchKind.Message);

        using (var scope = _fixture.CreateScope()) {
            var registry = scope.ServiceProvider.GetRequiredService<IProcessRegistry>();
            await registry.RegisterAsync<ApprovalProcess>();
        }

        var order   = await CreateOrderAsync();
        var process = await StartAsync(order);
        await CompleteAsync(process);
        var token   = await ReadTokenAsync(process.Name!);
        Assert.Equal("Await_Review", token.WaitingAtName);

        var dispatch = new EventDispatchContext();
        dispatch.SetSubscriptions([
            new() {
                Target         = process.CanonicalName!,
                EventType      = nameof(ApprovalProcess.Payment),
                CorrelationKey = process.CanonicalName,
                Token          = token.CanonicalName,
            },
        ]);

        using (var scope = _fixture.CreateScope()) {
            var handler = new FlowEventHandler(scope.ServiceProvider, dispatch);
            await handler.HandleAsync(new ApprovalPayload { Approved = true }, CancellationToken.None);
        }

        var advanced = await ReadTokenAsync(process.Name!);
        Assert.Equal("Approved", advanced.StateName);
        Assert.Null(advanced.WaitingAtName);
    }

    private async Task<Order> CreateOrderAsync() {
        using var scope      = _fixture.CreateScope();
        var       repository = scope.ServiceProvider.GetRequiredService<IRepository<Order>>();
        var order = new Order {
            Uid           = Guid.NewGuid(),
            Name          = Guid.NewGuid().ToString("n"),
            CanonicalName = $"orders/{Guid.NewGuid():n}",
            Timestamp     = Guid.NewGuid(),
            State         = "new",
        };

        await repository.AddAsync(order);
        await repository.CommitAsync();
        return order;
    }

    private async Task<SchemataProcess> StartAsync(Order order) {
        using var scope      = _fixture.CreateScope();
        var       repository = scope.ServiceProvider.GetRequiredService<IRepository<Order>>();
        var       current    = await repository.FindAsync([order.Uid]);
        Assert.NotNull(current);
        var runner = scope.ServiceProvider.GetRequiredService<FlowRunner>();
        return await runner.StartAsync(nameof(ApprovalProcess), current, null, null, CancellationToken.None);
    }

    private async Task CompleteAsync(SchemataProcess process) {
        using var scope  = _fixture.CreateScope();
        var       runner = scope.ServiceProvider.GetRequiredService<FlowRunner>();
        await runner.CompleteAsync(process, null, null, CancellationToken.None);
    }

    private async Task<SchemataProcessToken> ReadTokenAsync(string process) {
        using var scope      = _fixture.CreateScope();
        var       repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcessToken>>();
        var token = await repository.FirstOrDefaultAsync(query => query.Where(current => current.Process == process));
        Assert.NotNull(token);
        return token;
    }
}