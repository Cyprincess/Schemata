using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Actor.Foundation.Runtime;
using Schemata.Actor.Foundation.Tests.Fixtures;
using Schemata.Messaging.Skeleton;
using Xunit;

using Schemata.Abstractions.Advisors;
namespace Schemata.Actor.Foundation.Tests;

[Trait("Layer", "Integration")]
public class RequestDispatchingActorShould
{
    [Fact]
    public async Task Ask_WithDerivedPayload_UsesDeclaredBaseHandlerAndRouteKey() {
        var ledger = new Ledger();
        var (system, registry, root) = ActorSystemFactory.Create(services => {
            services.AddKeyedScoped<IRequestHandler<Credit, int>>("selected", (_, _) => new CreditHandler(ledger, 1));
            services.AddKeyedScoped<IRequestHandler<Credit, int>>("other", (_, _) => new CreditHandler(ledger, 100));
            services.AddKeyedScoped<IRequestHandler<BonusCredit, int>>("selected", (_, _) => new BonusHandler(ledger));
        });
        registry.Register("ledger", new(typeof(RequestDispatchingActor), ["selected"]));
        try {
            var actor = await system.GetAsync(new("ledger", "base"));
            var response = await actor.AskAsync<Credit, int>(new BonusCredit(7, 90), timeout: TimeSpan.FromSeconds(30));

            Assert.Equal(7, response);
            Assert.Equal(7, ledger.Balance);
        } finally {
            await system.ShutdownAsync(default);
            if (root is IAsyncDisposable disposable) await disposable.DisposeAsync();
        }
    }

    [Fact]
    public async Task Ask_WithMultipleResponseContracts_SelectsEachDeclaredHandler() {
        var ledger = new Ledger();
        var (system, registry, root) = ActorSystemFactory.Create(services => {
            services.AddKeyedScoped<IRequestHandler<Adjustment, int>>("selected", (_, _) => new AdjustmentHandler(ledger));
            services.AddKeyedScoped<IRequestHandler<Adjustment, string>>("selected", (_, _) => new AdjustmentHandler(ledger));
        });
        registry.Register("ledger", new(typeof(RequestDispatchingActor), ["selected"]));
        try {
            var actor = await system.GetAsync(new("ledger", "multiple"));
            var numeric = await actor.AskAsync<Adjustment, int>(new(4), timeout: TimeSpan.FromSeconds(30));
            var receipt = await actor.AskAsync<Adjustment, string>(new(3), timeout: TimeSpan.FromSeconds(30));

            Assert.Equal(4, numeric);
            Assert.Equal("balance=7", receipt);
            Assert.Equal(7, ledger.Balance);
        } finally {
            await system.ShutdownAsync(default);
            if (root is IAsyncDisposable disposable) await disposable.DisposeAsync();
        }
    }

    [Fact]
    public async Task Tell_WithExplicitRequestInterface_DispatchesAmbiguousPayloadThroughThatContract() {
        var ledger = new Ledger();
        var (system, registry, root) = ActorSystemFactory.Create(services => {
            services.AddKeyedScoped<IRequestHandler<IRequest<int>, int>>("selected", (_, _) => new InterfaceAdjustmentHandler(ledger));
            services.AddKeyedScoped<IRequestHandler<ReadBalance, int>>("selected", (_, _) => new ReadBalanceHandler(ledger));
        });
        registry.Register("ledger", new(typeof(RequestDispatchingActor), ["selected"]));
        try {
            var actor = await system.GetAsync(new("ledger", "tell"));
            await actor.TellAsync<IRequest<int>>(new Adjustment(6));
            var balance = await actor.AskAsync<ReadBalance, int>(new(), timeout: TimeSpan.FromSeconds(30));

            Assert.Equal(6, balance);
            Assert.Equal(6, ledger.Balance);
        } finally {
            await system.ShutdownAsync(default);
            if (root is IAsyncDisposable disposable) await disposable.DisposeAsync();
        }
    }

    [Fact]
    public async Task Ask_WithExactRequest_UsesKeyedHandler() {
        var ledger = new Ledger();
        var (system, registry, root) = ActorSystemFactory.Create(services => {
            services.AddKeyedScoped<IRequestHandler<Credit, int>>("selected", (_, _) => new CreditHandler(ledger, 1));
        });
        registry.Register("ledger", new(typeof(RequestDispatchingActor), ["selected"]));
        try {
            var actor = await system.GetAsync(new("ledger", "exact"));
            var balance = await actor.AskAsync<Credit, int>(new(5), timeout: TimeSpan.FromSeconds(30));

            Assert.Equal(5, balance);
            Assert.Equal(5, ledger.Balance);
        } finally {
            await system.ShutdownAsync(default);
            if (root is IAsyncDisposable disposable) await disposable.DisposeAsync();
        }
    }

    [Fact]
    public async Task Tell_WithAmbiguousDeclaredRequest_DoesNotInvokeEitherResponseHandler() {
        var ledger = new Ledger();
        var (system, registry, root) = ActorSystemFactory.Create(services => {
            services.AddKeyedScoped<IRequestHandler<Adjustment, int>>("selected", (_, _) => new AdjustmentHandler(ledger));
            services.AddKeyedScoped<IRequestHandler<Adjustment, string>>("selected", (_, _) => new AdjustmentHandler(ledger));
            services.AddKeyedScoped<IRequestHandler<ReadBalance, int>>("selected", (_, _) => new ReadBalanceHandler(ledger));
        });
        registry.Register("ledger", new(typeof(RequestDispatchingActor), ["selected"]));
        try {
            var actor = await system.GetAsync(new("ledger", "ambiguous"));
            await actor.TellAsync(new Adjustment(8));
            var balance = await actor.AskAsync<ReadBalance, int>(new(), timeout: TimeSpan.FromSeconds(30));

            Assert.Equal(0, balance);
            Assert.Equal(0, ledger.Balance);
        } finally {
            await system.ShutdownAsync(default);
            if (root is IAsyncDisposable disposable) await disposable.DisposeAsync();
        }
    }

    private sealed class Ledger
    {
        public int Balance { get; set; }
    }

    private record Credit(int Amount) : IRequest<int>;
    private sealed record BonusCredit(int Amount, int Bonus) : Credit(Amount);
    private sealed record Adjustment(int Amount) : IRequest<int>, IRequest<string>;
    private sealed record ReadBalance : IRequest<int>;

    private sealed class CreditHandler(Ledger ledger, int multiplier) : IRequestHandler<Credit, int>
    {
        public Task<int> HandleAsync(Credit request, AdviceContext context, CancellationToken ct = default) {
            ledger.Balance += request.Amount * multiplier;
            return Task.FromResult(ledger.Balance);
        }
    }

    private sealed class BonusHandler(Ledger ledger) : IRequestHandler<BonusCredit, int>
    {
        public Task<int> HandleAsync(BonusCredit request, AdviceContext context, CancellationToken ct = default) {
            ledger.Balance += request.Amount + request.Bonus;
            return Task.FromResult(ledger.Balance);
        }
    }

    private sealed class AdjustmentHandler(Ledger ledger) : IRequestHandler<Adjustment, int>, IRequestHandler<Adjustment, string>
    {
        Task<int> IRequestHandler<Adjustment, int>.HandleAsync(Adjustment request, AdviceContext context, CancellationToken ct) {
            ledger.Balance += request.Amount;
            return Task.FromResult(ledger.Balance);
        }

        Task<string> IRequestHandler<Adjustment, string>.HandleAsync(Adjustment request, AdviceContext context, CancellationToken ct) {
            ledger.Balance += request.Amount;
            return Task.FromResult($"balance={ledger.Balance}");
        }
    }

    private sealed class InterfaceAdjustmentHandler(Ledger ledger) : IRequestHandler<IRequest<int>, int>
    {
        public Task<int> HandleAsync(IRequest<int> request, AdviceContext context, CancellationToken ct = default) {
            ledger.Balance += ((Adjustment)request).Amount;
            return Task.FromResult(ledger.Balance);
        }
    }

    private sealed class ReadBalanceHandler(Ledger ledger) : IRequestHandler<ReadBalance, int>
    {
        public Task<int> HandleAsync(ReadBalance request, AdviceContext context, CancellationToken ct = default) => Task.FromResult(ledger.Balance);
    }
}
