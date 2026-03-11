using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Abstractions.Advisors;

public interface IAdvisor : IFeature;

public interface IAdvisor<in T1> : IAdvisor
{
    Task<AdviseResult> AdviseAsync(AdviceContext ctx, T1 a1, CancellationToken ct = default);
}

public interface IAdvisor<in T1, in T2> : IAdvisor
{
    Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        CancellationToken ct = default
    );
}

public interface IAdvisor<in T1, in T2, in T3> : IAdvisor
{
    Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        T3                a3,
        CancellationToken ct = default
    );
}

public interface IAdvisor<in T1, in T2, in T3, in T4> : IAdvisor
{
    Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        T3                a3,
        T4                a4,
        CancellationToken ct = default
    );
}

public interface IAdvisor<in T1, in T2, in T3, in T4, in T5> : IAdvisor
{
    Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        T3                a3,
        T4                a4,
        T5                a5,
        CancellationToken ct = default
    );
}

public interface IAdvisor<in T1, in T2, in T3, in T4, in T5, in T6> : IAdvisor
{
    Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        T3                a3,
        T4                a4,
        T5                a5,
        T6                a6,
        CancellationToken ct = default
    );
}

public interface IAdvisor<in T1, in T2, in T3, in T4, in T5, in T6, in T7> : IAdvisor
{
    Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        T3                a3,
        T4                a4,
        T5                a5,
        T6                a6,
        T7                a7,
        CancellationToken ct = default
    );
}

public interface IAdvisor<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8> : IAdvisor
{
    Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        T3                a3,
        T4                a4,
        T5                a5,
        T6                a6,
        T7                a7,
        T8                a8,
        CancellationToken ct = default
    );
}

public interface IAdvisor<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9> : IAdvisor
{
    Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        T3                a3,
        T4                a4,
        T5                a5,
        T6                a6,
        T7                a7,
        T8                a8,
        T9                a9,
        CancellationToken ct = default
    );
}

public interface IAdvisor<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10> : IAdvisor
{
    Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        T3                a3,
        T4                a4,
        T5                a5,
        T6                a6,
        T7                a7,
        T8                a8,
        T9                a9,
        T10               a10,
        CancellationToken ct = default
    );
}

public interface IAdvisor<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11> : IAdvisor
{
    Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        T3                a3,
        T4                a4,
        T5                a5,
        T6                a6,
        T7                a7,
        T8                a8,
        T9                a9,
        T10               a10,
        T11               a11,
        CancellationToken ct = default
    );
}

public interface IAdvisor<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12> : IAdvisor
{
    Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        T3                a3,
        T4                a4,
        T5                a5,
        T6                a6,
        T7                a7,
        T8                a8,
        T9                a9,
        T10               a10,
        T11               a11,
        T12               a12,
        CancellationToken ct = default
    );
}

public interface IAdvisor<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13> : IAdvisor
{
    Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        T3                a3,
        T4                a4,
        T5                a5,
        T6                a6,
        T7                a7,
        T8                a8,
        T9                a9,
        T10               a10,
        T11               a11,
        T12               a12,
        T13               a13,
        CancellationToken ct = default
    );
}

public interface IAdvisor<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, in T14> : IAdvisor
{
    Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        T3                a3,
        T4                a4,
        T5                a5,
        T6                a6,
        T7                a7,
        T8                a8,
        T9                a9,
        T10               a10,
        T11               a11,
        T12               a12,
        T13               a13,
        T14               a14,
        CancellationToken ct = default
    );
}

public interface IAdvisor<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, in T14, in T15> : IAdvisor
{
    Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        T3                a3,
        T4                a4,
        T5                a5,
        T6                a6,
        T7                a7,
        T8                a8,
        T9                a9,
        T10               a10,
        T11               a11,
        T12               a12,
        T13               a13,
        T14               a14,
        T15               a15,
        CancellationToken ct = default
    );
}

public interface IAdvisor<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, in T14, in T15, in T16> : IAdvisor
{
    Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        T3                a3,
        T4                a4,
        T5                a5,
        T6                a6,
        T7                a7,
        T8                a8,
        T9                a9,
        T10               a10,
        T11               a11,
        T12               a12,
        T13               a13,
        T14               a14,
        T15               a15,
        T16               a16,
        CancellationToken ct = default
    );
}
