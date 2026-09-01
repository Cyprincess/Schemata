using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Messaging.Skeleton.Advisors;

namespace Schemata.Messaging.Skeleton.Tests.Fixtures;

/// <summary>A command carrying a result, so the dispatcher's return path is observable.</summary>
public sealed record RenameWidget(string Name) : ICommand<string>;

/// <summary>A command with no result, exercising the <see cref="Schemata.Abstractions.Unit" /> path.</summary>
public sealed record RetireWidget(string Name) : ICommand;

/// <summary>A query, so the query pipeline chain has a payload that is not a command.</summary>
public sealed record CountWidgets : IQuery<int>;

/// <summary>A plain request that is neither a command nor a query, so no pipeline chain runs for it.</summary>
public sealed record PlainRequest(string Value) : IRequest<string>;

/// <summary>
///     Records the order of its before segment, the continuation, and its after segment onto a
///     shared trail, and appends a suffix to the response so the after segment's rewrite is
///     observable.
/// </summary>
public sealed class TracingPipelineAdvisor(List<string> trail) : IRequestPipelineAdvisor<RenameWidget, string>
{
    public int Order => 0;

    public async Task<string> AdviseAsync(
        AdviceContext                      ctx,
        RenameWidget                       request,
        RequestHandlerContinuation<string> next,
        CancellationToken                  ct = default) {
        trail.Add("before");
        var response = await next(ct);
        trail.Add("after");
        return $"{response}::after";
    }
}

/// <summary>Returns its own value without calling the continuation, proving the short-circuit path.</summary>
public sealed class ShortCircuitPipelineAdvisor(string value) : IRequestPipelineAdvisor<RenameWidget, string>
{
    public int Order => 0;

    public Task<string> AdviseAsync(
        AdviceContext                      ctx,
        RenameWidget                       request,
        RequestHandlerContinuation<string> next,
        CancellationToken                  ct = default) {
        return Task.FromResult(value);
    }
}

/// <summary>
///     A configurable wrap advisor over <see cref="RenameWidget" />. It records the ambient context
///     it observed and appends before and after markers to a shared trail. When
///     <paramref name="callNext" /> is <see langword="false" /> it returns its own short-circuit
///     value without invoking the continuation.
/// </summary>
public sealed class OrderedRenameAdvisor(int order, string tag, List<string> trail, bool callNext)
    : IRequestPipelineAdvisor<RenameWidget, string>
{
    public int Order => order;

    public AdviceContext? ObservedContext { get; private set; }

    public async Task<string> AdviseAsync(
        AdviceContext                      ctx,
        RenameWidget                       request,
        RequestHandlerContinuation<string> next,
        CancellationToken                  ct = default) {
        ObservedContext = ctx;
        trail.Add($"{tag}:before");
        if (!callNext) {
            return $"{tag}:short";
        }

        var response = await next(ct);
        trail.Add($"{tag}:after");
        return response;
    }
}

/// <summary>Throws from its before segment, so the dispatcher surfaces the advisor's own exception.</summary>
public sealed class ThrowingRenameAdvisor(Exception error) : IRequestPipelineAdvisor<RenameWidget, string>
{
    public int Order => 0;

    public Task<string> AdviseAsync(
        AdviceContext                      ctx,
        RenameWidget                       request,
        RequestHandlerContinuation<string> next,
        CancellationToken                  ct = default) {
        throw error;
    }
}

/// <summary>Appends its own tag to a shared trail, so a query pipeline chain running is observable.</summary>
public sealed class QueryTracingPipelineAdvisor(string tag, List<string> trail) : IRequestPipelineAdvisor<CountWidgets, int>
{
    public int Order => 0;

    public Task<int> AdviseAsync(
        AdviceContext                   ctx,
        CountWidgets                    request,
        RequestHandlerContinuation<int> next,
        CancellationToken               ct = default) {
        trail.Add(tag);
        return next(ct);
    }
}

/// <summary>Records whether it ran; registered against <see cref="PlainRequest" /> to prove a plain request never runs a chain.</summary>
public sealed class RecordingPlainPipelineAdvisor : IRequestPipelineAdvisor<PlainRequest, string>
{
    public int Order => 0;

    public bool Ran { get; private set; }

    public Task<string> AdviseAsync(
        AdviceContext                      ctx,
        PlainRequest                       request,
        RequestHandlerContinuation<string> next,
        CancellationToken                  ct = default) {
        Ran = true;
        return next(ct);
    }
}

/// <summary>An empty provider for advisor-unit tests that never resolve a service.</summary>
public sealed class EmptyServiceProvider : IServiceProvider
{
    public object? GetService(Type serviceType) => null;
}
