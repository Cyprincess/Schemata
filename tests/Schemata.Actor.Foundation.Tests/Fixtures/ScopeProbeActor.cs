using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Actor.Skeleton;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

/// <summary>Answers with the id of its turn-scoped <see cref="ScopedMarker" /> or whether an ambient <see cref="AdviceContext" /> for this turn's scope is observable.</summary>
public sealed class ScopeProbeActor : IActor
{
    public ValueTask OnStartedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope)
        => envelope.Payload switch {
            GetMarkerId    => ctx.ReplyAsync(ctx.Services.GetRequiredService<ScopedMarker>().Id),
            CaptureAmbient => ctx.ReplyAsync(AdviceContext.Current is not null && ReferenceEquals(AdviceContext.Current.ServiceProvider, ctx.Services)),
            _              => ValueTask.CompletedTask,
        };

    public ValueTask OnStoppedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) => ValueTask.FromResult(true);
}