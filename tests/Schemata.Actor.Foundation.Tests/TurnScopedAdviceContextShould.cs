using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Actor.Foundation.Runtime;
using Schemata.Actor.Foundation.Tests.Fixtures;
using Schemata.Messaging.Skeleton;
using Xunit;

namespace Schemata.Actor.Foundation.Tests;

/// <summary>
///     A request dispatched from an actor turn receives an explicit <see cref="AdviceContext" />
///     bound to that turn's scope, and every turn gets its own.
/// </summary>
public class TurnScopedAdviceContextShould
{
    [Fact]
    public async Task RequestDispatchingActor_HandsTheHandler_AContextBoundToTheTurnScope() {
        var (system, registry, root) = ActorSystemFactory.Create(services => {
            services.AddSingleton<MarkerRegistry>();
            services.AddScoped<ScopedMarker>();
            services.AddKeyedScoped<IRequestHandler<ProbeMarker, MarkerProbe>>("selected",
                (provider, _) => new MarkerProbeHandler(provider.GetRequiredService<ScopedMarker>()));
        });
        registry.Register("probe", new(typeof(RequestDispatchingActor), ["selected"]));
        try {
            var actor = await system.GetAsync(new("probe", "context"));

            var first  = await actor.AskAsync<ProbeMarker, MarkerProbe>(root, new(), timeout: TimeSpan.FromSeconds(30));
            var second = await actor.AskAsync<ProbeMarker, MarkerProbe>(root, new(), timeout: TimeSpan.FromSeconds(30));

            Assert.True(first.SameScope);
            Assert.True(second.SameScope);
            Assert.NotEqual(first.Id, second.Id);
        } finally {
            await system.ShutdownAsync(default);
            if (root is IAsyncDisposable disposable) await disposable.DisposeAsync();
        }
    }

    [Fact]
    public async Task RequestDispatchingActor_ResolvesAFreshHandlerAndLedgerFromEachTurnScope() {
        var (system, registry, root) = ActorSystemFactory.Create(services => {
            services.AddScoped<ScopeLedger>();
            services.AddKeyedScoped<IRequestHandler<ScopeProbe, ScopeReceipt>>("selected",
                (provider, _) => new ScopeProbeHandler(provider.GetRequiredService<ScopeLedger>()));
        });
        registry.Register("probe", new(typeof(RequestDispatchingActor), ["selected"]));
        try {
            var actor = await system.GetAsync(new("probe", "handler"));

            var first  = await actor.AskAsync<ScopeProbe, ScopeReceipt>(root, new(), timeout: TimeSpan.FromSeconds(30));
            var second = await actor.AskAsync<ScopeProbe, ScopeReceipt>(root, new(), timeout: TimeSpan.FromSeconds(30));

            Assert.Equal(1, first.Balance);
            Assert.Equal(1, second.Balance);
            Assert.NotEqual(first.LedgerId, second.LedgerId);
            Assert.NotEqual(first.HandlerId, second.HandlerId);
        } finally {
            await system.ShutdownAsync(default);
            if (root is IAsyncDisposable disposable) await disposable.DisposeAsync();
        }
    }

    private sealed record ScopeProbe : IRequest<ScopeReceipt>;

    private sealed record ScopeReceipt(Guid LedgerId, Guid HandlerId, int Balance);

    private sealed class ScopeLedger
    {
        public Guid Id { get; } = Guid.NewGuid();
        public int Balance { get; set; }
    }

    private sealed class ScopeProbeHandler(ScopeLedger ledger) : IRequestHandler<ScopeProbe, ScopeReceipt>
    {
        private readonly Guid _id = Guid.NewGuid();

        public Task<ScopeReceipt> HandleAsync(ScopeProbe request, AdviceContext context, CancellationToken ct = default) {
            ledger.Balance++;
            return Task.FromResult(new ScopeReceipt(ledger.Id, _id, ledger.Balance));
        }
    }

    private sealed record ProbeMarker : IRequest<MarkerProbe>;

    private sealed record MarkerProbe(bool SameScope, Guid Id);

    private sealed class MarkerProbeHandler(ScopedMarker marker) : IRequestHandler<ProbeMarker, MarkerProbe>
    {
        public Task<MarkerProbe> HandleAsync(ProbeMarker request, AdviceContext context, CancellationToken ct = default) {
            var scoped = context.ServiceProvider.GetRequiredService<ScopedMarker>();
            return Task.FromResult(new MarkerProbe(ReferenceEquals(marker, scoped), scoped.Id));
        }
    }
}
