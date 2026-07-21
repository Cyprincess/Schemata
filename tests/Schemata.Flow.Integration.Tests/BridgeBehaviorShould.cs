using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Event.Foundation.Internal;
using Schemata.Event.Skeleton;
using Schemata.Event.Skeleton.Entities;
using Schemata.Flow.Event.Internal;
using Schemata.Flow.Foundation;
using Schemata.Flow.Integration.Tests.Fixtures;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Builders;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
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
        _fixture.FlowOptions.Bridges.Add(SchemataFlowOptions.EventsBridge);

        using (var scope = _fixture.CreateScope()) {
            var registry = scope.ServiceProvider.GetRequiredService<IProcessRegistry>();
            await registry.RegisterAsync<ApprovalProcess>();
        }

        var order   = await CreateOrderAsync();
        var process = await StartAsync(order);
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
            Uid           = Identifiers.NewUid(),
            Name          = Identifiers.NewUid().ToString("n"),
            CanonicalName = $"orders/{Identifiers.NewUid():n}",
            Timestamp     = Identifiers.NewUid(),
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

    private async Task<SchemataProcessToken> ReadTokenAsync(string process) {
        using var scope      = _fixture.CreateScope();
        var       repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcessToken>>();
        var token = await repository.FirstOrDefaultAsync(query => query.Where(current => current.Process == process));
        Assert.NotNull(token);
        return token!;
    }
}

public sealed class ApprovalProcess : ProcessDefinition
{
    public ApprovalProcess() {
        BindSource<Order>(projection: FlowSourceProjection.None);
        this.Start().Go(Review);
        this.During(Review).Await(
            this.On(Payment).Decide(
                this.When<Order, ApprovalPayload>(Payment, (_, payload) => payload.Approved).Go(Approved),
                this.Otherwise().Go(Rejected)));
        this.During(Approved).End();
        this.During(Rejected).End();
    }

    public NoneTask Review { get; } = null!;

    public UserTask Approved { get; } = null!;

    public UserTask Rejected { get; } = null!;

    public Message<ApprovalPayload> Payment { get; } = null!;
}

public sealed class ApprovalPayload : IEvent
{
    public bool Approved { get; init; }
}
