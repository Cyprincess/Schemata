using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Push.Skeleton;

namespace Schemata.Push.Tests;

/// <summary>
///     Configurable <see cref="IPushTransport" /> test double. Records the contexts it observes,
///     returns a caller-supplied outcome, and can delay or throw to exercise streaming order and
///     isolation. Test-only; never shipped in <c>src</c>.
/// </summary>
public sealed class FakeTransport : IPushTransport
{
    private readonly Func<PushContext, CancellationToken, ValueTask<TransportResult>> _handler;

    public FakeTransport(string name, Func<PushContext, CancellationToken, ValueTask<TransportResult>> handler) {
        Name     = name;
        _handler = handler;
    }

    public int Invocations { get; private set; }

    public PushContext? Last { get; private set; }

    public string Name { get; }

    public ValueTask<TransportResult> TrySendAsync(PushContext context, CancellationToken ct = default) {
        Invocations++;
        Last = context;
        return _handler(context, ct);
    }

    /// <summary>A transport that always reports <see cref="TransportStatus.Sent" />.</summary>
    public static FakeTransport Sending(string name) {
        return new(name, (_, _) => new(TransportResult.Sent(name)));
    }

    /// <summary>A transport that always reports <see cref="TransportStatus.Skipped" />.</summary>
    public static FakeTransport Skipping(string name) {
        return new(name, (_, _) => new(TransportResult.Skipped(name)));
    }

    /// <summary>A transport that reports <see cref="TransportStatus.Sent" /> after a delay.</summary>
    public static FakeTransport SendingAfter(string name, TimeSpan delay) {
        return new(name, async (_, ct) => {
            await Task.Delay(delay, ct);
            return TransportResult.Sent(name);
        });
    }

    /// <summary>A transport that throws, exercising isolation.</summary>
    public static FakeTransport Throwing(string name) {
        return new(name, (_, _) => throw new InvalidOperationException($"transport '{name}' failed"));
    }

    /// <summary>
    ///     A transport that handles only the target types it claims, reporting
    ///     <see cref="TransportStatus.Sent" /> on a match and <see cref="TransportStatus.Skipped" /> otherwise.
    /// </summary>
    public static FakeTransport Filtering<TTarget>(string name)
        where TTarget : PushTarget {
        return new(name, (context, _) => new(
            context.Target is TTarget
                ? TransportResult.Sent(name)
                : TransportResult.Skipped(name)));
    }
}
