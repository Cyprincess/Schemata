using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;

namespace Schemata.Advice.Generator.Tests;

public interface IArity1TestAdvisor : IAdvisor<int>;

public interface IArity2TestAdvisor : IAdvisor<int, int>;

public interface IArity3TestAdvisor : IAdvisor<int, int, int>;

public interface IArity4TestAdvisor : IAdvisor<int, int, int, int>;

public interface IArity5TestAdvisor : IAdvisor<int, int, int, int, int>;

public interface IArity6TestAdvisor : IAdvisor<int, int, int, int, int, int>;

public interface IArity7TestAdvisor : IAdvisor<int, int, int, int, int, int, int>;

public interface IArity8TestAdvisor : IAdvisor<int, int, int, int, int, int, int, int>;

public interface IArity9TestAdvisor : IAdvisor<int, int, int, int, int, int, int, int, int>;

public interface IArity10TestAdvisor : IAdvisor<int, int, int, int, int, int, int, int, int, int>;

public interface IArity11TestAdvisor : IAdvisor<int, int, int, int, int, int, int, int, int, int, int>;

public interface IArity12TestAdvisor : IAdvisor<int, int, int, int, int, int, int, int, int, int, int, int>;

public interface IArity13TestAdvisor : IAdvisor<int, int, int, int, int, int, int, int, int, int, int, int, int>;

public interface IArity14TestAdvisor : IAdvisor<int, int, int, int, int, int, int, int, int, int, int, int, int, int>;

public interface IArity15TestAdvisor : IAdvisor<int, int, int, int, int, int, int, int, int, int, int, int, int, int, int>;

public interface IArity16TestAdvisor : IAdvisor<int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, int>;

internal sealed class AdvisorRecorder
{
    public List<int> Calls { get; } = [];

    public List<int> Arguments { get; } = [];
}

internal abstract class RecordingAdvisor(AdvisorRecorder recorder, int order, AdviseResult result)
{
    public int Order { get; } = order;

    protected Task<AdviseResult> Record(params int[] arguments) {
        recorder.Calls.Add(Order);
        recorder.Arguments.AddRange(arguments);
        return Task.FromResult(result);
    }
}

internal sealed class Arity1Advisor(AdvisorRecorder recorder, int order, AdviseResult result)
    : RecordingAdvisor(recorder, order, result), IArity1TestAdvisor
{
    public Task<AdviseResult> AdviseAsync(AdviceContext ctx, int a1, CancellationToken ct = default) => Record(a1);
}

internal sealed class Arity2Advisor(AdvisorRecorder recorder, int order, AdviseResult result)
    : RecordingAdvisor(recorder, order, result), IArity2TestAdvisor
{
    public Task<AdviseResult> AdviseAsync(AdviceContext ctx, int a1, int a2, CancellationToken ct = default) => Record(a1, a2);
}

internal sealed class Arity3Advisor(AdvisorRecorder recorder, int order, AdviseResult result)
    : RecordingAdvisor(recorder, order, result), IArity3TestAdvisor
{
    public Task<AdviseResult> AdviseAsync(AdviceContext ctx, int a1, int a2, int a3, CancellationToken ct = default) => Record(a1, a2, a3);
}

internal sealed class Arity4Advisor(AdvisorRecorder recorder, int order, AdviseResult result)
    : RecordingAdvisor(recorder, order, result), IArity4TestAdvisor
{
    public Task<AdviseResult> AdviseAsync(AdviceContext ctx, int a1, int a2, int a3, int a4, CancellationToken ct = default) => Record(a1, a2, a3, a4);
}

internal sealed class Arity5Advisor(AdvisorRecorder recorder, int order, AdviseResult result)
    : RecordingAdvisor(recorder, order, result), IArity5TestAdvisor
{
    public Task<AdviseResult> AdviseAsync(AdviceContext ctx, int a1, int a2, int a3, int a4, int a5, CancellationToken ct = default) => Record(a1, a2, a3, a4, a5);
}

internal sealed class Arity6Advisor(AdvisorRecorder recorder, int order, AdviseResult result)
    : RecordingAdvisor(recorder, order, result), IArity6TestAdvisor
{
    public Task<AdviseResult> AdviseAsync(AdviceContext ctx, int a1, int a2, int a3, int a4, int a5, int a6, CancellationToken ct = default) => Record(a1, a2, a3, a4, a5, a6);
}

internal sealed class Arity7Advisor(AdvisorRecorder recorder, int order, AdviseResult result)
    : RecordingAdvisor(recorder, order, result), IArity7TestAdvisor
{
    public Task<AdviseResult> AdviseAsync(AdviceContext ctx, int a1, int a2, int a3, int a4, int a5, int a6, int a7, CancellationToken ct = default) => Record(a1, a2, a3, a4, a5, a6, a7);
}

internal sealed class Arity8Advisor(AdvisorRecorder recorder, int order, AdviseResult result)
    : RecordingAdvisor(recorder, order, result), IArity8TestAdvisor
{
    public Task<AdviseResult> AdviseAsync(AdviceContext ctx, int a1, int a2, int a3, int a4, int a5, int a6, int a7, int a8, CancellationToken ct = default) => Record(a1, a2, a3, a4, a5, a6, a7, a8);
}

internal sealed class Arity9Advisor(AdvisorRecorder recorder, int order, AdviseResult result)
    : RecordingAdvisor(recorder, order, result), IArity9TestAdvisor
{
    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        int               a1,
        int               a2,
        int               a3,
        int               a4,
        int               a5,
        int               a6,
        int               a7,
        int               a8,
        int               a9,
        CancellationToken ct = default
    ) => Record(a1, a2, a3, a4, a5, a6, a7, a8, a9);
}

internal sealed class Arity10Advisor(AdvisorRecorder recorder, int order, AdviseResult result)
    : RecordingAdvisor(recorder, order, result), IArity10TestAdvisor
{
    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        int               a1,
        int               a2,
        int               a3,
        int               a4,
        int               a5,
        int               a6,
        int               a7,
        int               a8,
        int               a9,
        int               a10,
        CancellationToken ct = default
    ) => Record(a1, a2, a3, a4, a5, a6, a7, a8, a9, a10);
}

internal sealed class Arity11Advisor(AdvisorRecorder recorder, int order, AdviseResult result)
    : RecordingAdvisor(recorder, order, result), IArity11TestAdvisor
{
    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        int               a1,
        int               a2,
        int               a3,
        int               a4,
        int               a5,
        int               a6,
        int               a7,
        int               a8,
        int               a9,
        int               a10,
        int               a11,
        CancellationToken ct = default
    ) => Record(a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11);
}

internal sealed class Arity12Advisor(AdvisorRecorder recorder, int order, AdviseResult result)
    : RecordingAdvisor(recorder, order, result), IArity12TestAdvisor
{
    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        int               a1,
        int               a2,
        int               a3,
        int               a4,
        int               a5,
        int               a6,
        int               a7,
        int               a8,
        int               a9,
        int               a10,
        int               a11,
        int               a12,
        CancellationToken ct = default
    ) => Record(a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12);
}

internal sealed class Arity13Advisor(AdvisorRecorder recorder, int order, AdviseResult result)
    : RecordingAdvisor(recorder, order, result), IArity13TestAdvisor
{
    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        int               a1,
        int               a2,
        int               a3,
        int               a4,
        int               a5,
        int               a6,
        int               a7,
        int               a8,
        int               a9,
        int               a10,
        int               a11,
        int               a12,
        int               a13,
        CancellationToken ct = default
    ) => Record(a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13);
}

internal sealed class Arity14Advisor(AdvisorRecorder recorder, int order, AdviseResult result)
    : RecordingAdvisor(recorder, order, result), IArity14TestAdvisor
{
    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        int               a1,
        int               a2,
        int               a3,
        int               a4,
        int               a5,
        int               a6,
        int               a7,
        int               a8,
        int               a9,
        int               a10,
        int               a11,
        int               a12,
        int               a13,
        int               a14,
        CancellationToken ct = default
    ) => Record(a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14);
}

internal sealed class Arity15Advisor(AdvisorRecorder recorder, int order, AdviseResult result)
    : RecordingAdvisor(recorder, order, result), IArity15TestAdvisor
{
    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        int               a1,
        int               a2,
        int               a3,
        int               a4,
        int               a5,
        int               a6,
        int               a7,
        int               a8,
        int               a9,
        int               a10,
        int               a11,
        int               a12,
        int               a13,
        int               a14,
        int               a15,
        CancellationToken ct = default
    ) => Record(a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15);
}

internal sealed class Arity16Advisor(AdvisorRecorder recorder, int order, AdviseResult result)
    : RecordingAdvisor(recorder, order, result), IArity16TestAdvisor
{
    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        int               a1,
        int               a2,
        int               a3,
        int               a4,
        int               a5,
        int               a6,
        int               a7,
        int               a8,
        int               a9,
        int               a10,
        int               a11,
        int               a12,
        int               a13,
        int               a14,
        int               a15,
        int               a16,
        CancellationToken ct = default
    ) => Record(a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16);
}
