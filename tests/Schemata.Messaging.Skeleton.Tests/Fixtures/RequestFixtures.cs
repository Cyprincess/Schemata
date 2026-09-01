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

/// <summary>A query, so the query advisor chain has a payload that is not a command.</summary>
public sealed record CountWidgets : IQuery<int>;

/// <summary>A plain request that is neither a command nor a query, so no advisor chain runs for it.</summary>
public sealed record PlainRequest(string Value) : IRequest<string>;

/// <summary>
///     Appends its own <paramref name="tag" /> to a shared trail, then returns
///     <paramref name="result" />. Hand-written rather than a Moq proxy because the trail has to be
///     appended to from inside the advise call — which is what proves ordering — and because it
///     records the <see cref="AdviceContext" /> instance it observed, which the ambient-context
///     test compares against the one the handler sees.
/// </summary>
public sealed class TracingCommandAdvisor(int order, string tag, List<string> trail, AdviseResult result)
    : ICommandAdvisor<RenameWidget>
{
    public int Order => order;

    public AdviceContext? ObservedContext { get; private set; }

    public Task<AdviseResult> AdviseAsync(AdviceContext ctx, RenameWidget a1, CancellationToken ct = default) {
        ObservedContext = ctx;
        trail.Add(tag);
        return Task.FromResult(result);
    }
}

/// <summary>Records that it ran, returns <see cref="AdviseResult.Handle" /> after seeding the result.</summary>
public sealed class HandlingCommandAdvisor(string value) : ICommandAdvisor<RenameWidget>
{
    public int Order => 0;

    public bool Ran { get; private set; }

    public Task<AdviseResult> AdviseAsync(AdviceContext ctx, RenameWidget a1, CancellationToken ct = default) {
        Ran = true;
        ctx.Set(value);
        return Task.FromResult(AdviseResult.Handle);
    }
}

/// <summary>Returns <see cref="AdviseResult.Handle" /> without seeding a result, to prove the dispatcher guards it.</summary>
public sealed class UnsetHandlingCommandAdvisor : ICommandAdvisor<RenameWidget>
{
    public int Order => 0;

    public Task<AdviseResult> AdviseAsync(AdviceContext ctx, RenameWidget a1, CancellationToken ct = default) {
        return Task.FromResult(AdviseResult.Handle);
    }
}

/// <summary>Blocks every command it sees.</summary>
public sealed class BlockingCommandAdvisor : ICommandAdvisor<RenameWidget>
{
    public int Order => 0;

    public Task<AdviseResult> AdviseAsync(AdviceContext ctx, RenameWidget a1, CancellationToken ct = default) {
        return Task.FromResult(AdviseResult.Block);
    }
}

/// <summary>Appends its own tag to a shared trail, so a query advisor chain running is observable.</summary>
public sealed class TracingQueryAdvisor(string tag, List<string> trail) : IQueryAdvisor<CountWidgets>
{
    public int Order => 0;

    public Task<AdviseResult> AdviseAsync(AdviceContext ctx, CountWidgets a1, CancellationToken ct = default) {
        trail.Add(tag);
        return Task.FromResult(AdviseResult.Continue);
    }
}

/// <summary>Records if it ran; registered against <see cref="PlainRequest" /> to prove that a plain request never runs the command chain.</summary>
public sealed class RecordingCommandAdvisorForPlainRequest : ICommandAdvisor<PlainRequest>
{
    public int Order => 0;

    public bool Ran { get; private set; }

    public Task<AdviseResult> AdviseAsync(AdviceContext ctx, PlainRequest a1, CancellationToken ct = default) {
        Ran = true;
        return Task.FromResult(AdviseResult.Continue);
    }
}

/// <summary>Records if it ran; registered against <see cref="PlainRequest" /> to prove that a plain request never runs the query chain.</summary>
public sealed class RecordingQueryAdvisorForPlainRequest : IQueryAdvisor<PlainRequest>
{
    public int Order => 0;

    public bool Ran { get; private set; }

    public Task<AdviseResult> AdviseAsync(AdviceContext ctx, PlainRequest a1, CancellationToken ct = default) {
        Ran = true;
        return Task.FromResult(AdviseResult.Continue);
    }
}
