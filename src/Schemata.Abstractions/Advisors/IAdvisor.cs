using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Abstractions.Advisors;

/// <summary>
///     Base advisor interface. The advisor pipeline resolves
///     implementations from the DI container, sorts them in ascending
///     <see cref="Order" />, and invokes each in sequence.
/// </summary>
public interface IAdvisor
{
    /// <summary>
    ///     Ascending sort key. Advisors with lower values execute first.
    /// </summary>
    int Order { get; }
}

/// <summary>
///     Typed advisor receiving one argument.
/// </summary>
/// <typeparam name="T1">The first argument type.</typeparam>
public interface IAdvisor<in T1> : IAdvisor
{
    /// <summary>
    ///     Evaluates the advise request and returns a result controlling
    ///     the pipeline — <see cref="AdviseResult.Continue" /> to proceed,
    ///     <see cref="AdviseResult.Block" /> to abort,
    ///     or <see cref="AdviseResult.Handle" /> to short-circuit.
    /// </summary>
    /// <param name="ctx">The shared <see cref="AdviceContext" /> flowing through the pipeline.</param>
    /// <param name="a1">The first argument.</param>
    /// <param name="ct">A cancellation token.</param>
    Task<AdviseResult> AdviseAsync(AdviceContext ctx, T1 a1, CancellationToken ct = default);
}

/// <inheritdoc cref="IAdvisor{T1}" />
public interface IAdvisor<in T1, in T2> : IAdvisor
{
    /// <inheritdoc cref="IAdvisor{T1}.AdviseAsync" />
    Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        CancellationToken ct = default
    );
}

/// <inheritdoc cref="IAdvisor{T1}" />
public interface IAdvisor<in T1, in T2, in T3> : IAdvisor
{
    /// <inheritdoc cref="IAdvisor{T1}.AdviseAsync" />
    Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        T3                a3,
        CancellationToken ct = default
    );
}

/// <inheritdoc cref="IAdvisor{T1}" />
public interface IAdvisor<in T1, in T2, in T3, in T4> : IAdvisor
{
    /// <inheritdoc cref="IAdvisor{T1}.AdviseAsync" />
    Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        T3                a3,
        T4                a4,
        CancellationToken ct = default
    );
}

/// <inheritdoc cref="IAdvisor{T1}" />
public interface IAdvisor<in T1, in T2, in T3, in T4, in T5> : IAdvisor
{
    /// <inheritdoc cref="IAdvisor{T1}.AdviseAsync" />
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

/// <inheritdoc cref="IAdvisor{T1}" />
public interface IAdvisor<in T1, in T2, in T3, in T4, in T5, in T6> : IAdvisor
{
    /// <inheritdoc cref="IAdvisor{T1}.AdviseAsync" />
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

/// <inheritdoc cref="IAdvisor{T1}" />
public interface IAdvisor<in T1, in T2, in T3, in T4, in T5, in T6, in T7> : IAdvisor
{
    /// <inheritdoc cref="IAdvisor{T1}.AdviseAsync" />
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

/// <inheritdoc cref="IAdvisor{T1}" />
public interface IAdvisor<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8> : IAdvisor
{
    /// <inheritdoc cref="IAdvisor{T1}.AdviseAsync" />
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

/// <inheritdoc cref="IAdvisor{T1}" />
public interface IAdvisor<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9> : IAdvisor
{
    /// <inheritdoc cref="IAdvisor{T1}.AdviseAsync" />
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

/// <inheritdoc cref="IAdvisor{T1}" />
public interface IAdvisor<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10> : IAdvisor
{
    /// <inheritdoc cref="IAdvisor{T1}.AdviseAsync" />
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

/// <inheritdoc cref="IAdvisor{T1}" />
public interface IAdvisor<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11> : IAdvisor
{
    /// <inheritdoc cref="IAdvisor{T1}.AdviseAsync" />
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

/// <inheritdoc cref="IAdvisor{T1}" />
public interface
    IAdvisor<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12> : IAdvisor
{
    /// <inheritdoc cref="IAdvisor{T1}.AdviseAsync" />
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

/// <inheritdoc cref="IAdvisor{T1}" />
public interface IAdvisor<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13> : IAdvisor
{
    /// <inheritdoc cref="IAdvisor{T1}.AdviseAsync" />
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

/// <inheritdoc cref="IAdvisor{T1}" />
public interface IAdvisor<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, in T14> : IAdvisor
{
    /// <inheritdoc cref="IAdvisor{T1}.AdviseAsync" />
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

/// <inheritdoc cref="IAdvisor{T1}" />
public interface IAdvisor<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, in T14, in T15> : IAdvisor
{
    /// <inheritdoc cref="IAdvisor{T1}.AdviseAsync" />
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

/// <inheritdoc cref="IAdvisor{T1}" />
public interface IAdvisor<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, in T14, in T15, in T16> : IAdvisor
{
    /// <inheritdoc cref="IAdvisor{T1}.AdviseAsync" />
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
