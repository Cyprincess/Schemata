using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Push.Skeleton;
using Schemata.Push.Skeleton.Advisors;

namespace Schemata.Push.Tests;

/// <summary>
///     <see cref="IPushSendAdvisor" /> test double that records invocations and returns a
///     caller-supplied <see cref="AdviseResult" />. Test-only.
/// </summary>
public sealed class RecordingAdvisor(AdviseResult result, int order = 0) : IPushSendAdvisor
{
    public int Invocations { get; private set; }

    public PushContext? Last { get; private set; }

    public int Order => order;

    public Task<AdviseResult> AdviseAsync(AdviceContext ctx, PushContext a1, CancellationToken ct = default) {
        Invocations++;
        Last = a1;
        return Task.FromResult(result);
    }
}
