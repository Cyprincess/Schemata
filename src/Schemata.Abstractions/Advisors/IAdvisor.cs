using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Abstractions.Advisors;

/// <summary>
///     Marker interface for all advisor types that participate in the advice pipeline.
/// </summary>
public interface IAdvisor
{
    /// <summary>
    ///     Gets the execution order used to sort advisors.
    /// </summary>
    int Order { get; }
}

/// <summary>
///     An advisor that participates in the advice pipeline with one argument.
/// </summary>
/// <typeparam name="T1">The type of the first argument passed through the pipeline.</typeparam>
public interface IAdvisor<in T1> : IAdvisor
{
    /// <summary>
    ///     Executes this advisor's logic within the pipeline.
    /// </summary>
    /// <param name="ctx">The advice context containing shared state and services.</param>
    /// <param name="a1">The first argument.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An <see cref="AdviseResult" /> indicating whether the pipeline should continue, block, or be handled.</returns>
    Task<AdviseResult> AdviseAsync(AdviceContext ctx, T1 a1, CancellationToken ct = default);
}

/// <summary>
///     An advisor that participates in the advice pipeline with two arguments.
/// </summary>
/// <typeparam name="T1">The type of the first argument.</typeparam>
/// <typeparam name="T2">The type of the second argument.</typeparam>
public interface IAdvisor<in T1, in T2> : IAdvisor
{
    /// <summary>
    ///     Executes this advisor's logic within the pipeline.
    /// </summary>
    /// <param name="ctx">The advice context containing shared state and services.</param>
    /// <param name="a1">The first argument.</param>
    /// <param name="a2">The second argument.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An <see cref="AdviseResult" /> indicating whether the pipeline should continue, block, or be handled.</returns>
    Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        CancellationToken ct = default
    );
}

/// <summary>
///     An advisor that participates in the advice pipeline with three arguments.
/// </summary>
/// <typeparam name="T1">The type of the first argument.</typeparam>
/// <typeparam name="T2">The type of the second argument.</typeparam>
/// <typeparam name="T3">The type of the third argument.</typeparam>
public interface IAdvisor<in T1, in T2, in T3> : IAdvisor
{
    /// <summary>
    ///     Executes this advisor's logic within the pipeline.
    /// </summary>
    /// <param name="ctx">The advice context containing shared state and services.</param>
    /// <param name="a1">The first argument.</param>
    /// <param name="a2">The second argument.</param>
    /// <param name="a3">The third argument.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An <see cref="AdviseResult" /> indicating whether the pipeline should continue, block, or be handled.</returns>
    Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        T3                a3,
        CancellationToken ct = default
    );
}

/// <summary>
///     An advisor that participates in the advice pipeline with four arguments.
/// </summary>
/// <typeparam name="T1">The type of the first argument.</typeparam>
/// <typeparam name="T2">The type of the second argument.</typeparam>
/// <typeparam name="T3">The type of the third argument.</typeparam>
/// <typeparam name="T4">The type of the fourth argument.</typeparam>
public interface IAdvisor<in T1, in T2, in T3, in T4> : IAdvisor
{
    /// <summary>
    ///     Executes this advisor's logic within the pipeline.
    /// </summary>
    /// <param name="ctx">The advice context containing shared state and services.</param>
    /// <param name="a1">The first argument.</param>
    /// <param name="a2">The second argument.</param>
    /// <param name="a3">The third argument.</param>
    /// <param name="a4">The fourth argument.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An <see cref="AdviseResult" /> indicating whether the pipeline should continue, block, or be handled.</returns>
    Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        T3                a3,
        T4                a4,
        CancellationToken ct = default
    );
}

/// <summary>
///     An advisor that participates in the advice pipeline with five arguments.
/// </summary>
/// <typeparam name="T1">The type of the first argument.</typeparam>
/// <typeparam name="T2">The type of the second argument.</typeparam>
/// <typeparam name="T3">The type of the third argument.</typeparam>
/// <typeparam name="T4">The type of the fourth argument.</typeparam>
/// <typeparam name="T5">The type of the fifth argument.</typeparam>
public interface IAdvisor<in T1, in T2, in T3, in T4, in T5> : IAdvisor
{
    /// <summary>
    ///     Executes this advisor's logic within the pipeline.
    /// </summary>
    /// <param name="ctx">The advice context containing shared state and services.</param>
    /// <param name="a1">The first argument.</param>
    /// <param name="a2">The second argument.</param>
    /// <param name="a3">The third argument.</param>
    /// <param name="a4">The fourth argument.</param>
    /// <param name="a5">The fifth argument.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An <see cref="AdviseResult" /> indicating whether the pipeline should continue, block, or be handled.</returns>
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

/// <summary>
///     An advisor that participates in the advice pipeline with six arguments.
/// </summary>
/// <typeparam name="T1">The type of the first argument.</typeparam>
/// <typeparam name="T2">The type of the second argument.</typeparam>
/// <typeparam name="T3">The type of the third argument.</typeparam>
/// <typeparam name="T4">The type of the fourth argument.</typeparam>
/// <typeparam name="T5">The type of the fifth argument.</typeparam>
/// <typeparam name="T6">The type of the sixth argument.</typeparam>
public interface IAdvisor<in T1, in T2, in T3, in T4, in T5, in T6> : IAdvisor
{
    /// <summary>
    ///     Executes this advisor's logic within the pipeline.
    /// </summary>
    /// <param name="ctx">The advice context containing shared state and services.</param>
    /// <param name="a1">The first argument.</param>
    /// <param name="a2">The second argument.</param>
    /// <param name="a3">The third argument.</param>
    /// <param name="a4">The fourth argument.</param>
    /// <param name="a5">The fifth argument.</param>
    /// <param name="a6">The sixth argument.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An <see cref="AdviseResult" /> indicating whether the pipeline should continue, block, or be handled.</returns>
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

/// <summary>
///     An advisor that participates in the advice pipeline with seven arguments.
/// </summary>
/// <typeparam name="T1">The type of the first argument.</typeparam>
/// <typeparam name="T2">The type of the second argument.</typeparam>
/// <typeparam name="T3">The type of the third argument.</typeparam>
/// <typeparam name="T4">The type of the fourth argument.</typeparam>
/// <typeparam name="T5">The type of the fifth argument.</typeparam>
/// <typeparam name="T6">The type of the sixth argument.</typeparam>
/// <typeparam name="T7">The type of the seventh argument.</typeparam>
public interface IAdvisor<in T1, in T2, in T3, in T4, in T5, in T6, in T7> : IAdvisor
{
    /// <summary>
    ///     Executes this advisor's logic within the pipeline.
    /// </summary>
    /// <param name="ctx">The advice context containing shared state and services.</param>
    /// <param name="a1">The first argument.</param>
    /// <param name="a2">The second argument.</param>
    /// <param name="a3">The third argument.</param>
    /// <param name="a4">The fourth argument.</param>
    /// <param name="a5">The fifth argument.</param>
    /// <param name="a6">The sixth argument.</param>
    /// <param name="a7">The seventh argument.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An <see cref="AdviseResult" /> indicating whether the pipeline should continue, block, or be handled.</returns>
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

/// <summary>
///     An advisor that participates in the advice pipeline with eight arguments.
/// </summary>
/// <typeparam name="T1">The type of the first argument.</typeparam>
/// <typeparam name="T2">The type of the second argument.</typeparam>
/// <typeparam name="T3">The type of the third argument.</typeparam>
/// <typeparam name="T4">The type of the fourth argument.</typeparam>
/// <typeparam name="T5">The type of the fifth argument.</typeparam>
/// <typeparam name="T6">The type of the sixth argument.</typeparam>
/// <typeparam name="T7">The type of the seventh argument.</typeparam>
/// <typeparam name="T8">The type of the eighth argument.</typeparam>
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

/// <summary>
///     An advisor that participates in the advice pipeline with nine arguments.
/// </summary>
/// <typeparam name="T1">The type of the first argument.</typeparam>
/// <typeparam name="T2">The type of the second argument.</typeparam>
/// <typeparam name="T3">The type of the third argument.</typeparam>
/// <typeparam name="T4">The type of the fourth argument.</typeparam>
/// <typeparam name="T5">The type of the fifth argument.</typeparam>
/// <typeparam name="T6">The type of the sixth argument.</typeparam>
/// <typeparam name="T7">The type of the seventh argument.</typeparam>
/// <typeparam name="T8">The type of the eighth argument.</typeparam>
/// <typeparam name="T9">The type of the ninth argument.</typeparam>
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

/// <summary>
///     An advisor that participates in the advice pipeline with ten arguments.
/// </summary>
/// <typeparam name="T1">The type of the first argument.</typeparam>
/// <typeparam name="T2">The type of the second argument.</typeparam>
/// <typeparam name="T3">The type of the third argument.</typeparam>
/// <typeparam name="T4">The type of the fourth argument.</typeparam>
/// <typeparam name="T5">The type of the fifth argument.</typeparam>
/// <typeparam name="T6">The type of the sixth argument.</typeparam>
/// <typeparam name="T7">The type of the seventh argument.</typeparam>
/// <typeparam name="T8">The type of the eighth argument.</typeparam>
/// <typeparam name="T9">The type of the ninth argument.</typeparam>
/// <typeparam name="T10">The type of the tenth argument.</typeparam>
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

/// <summary>
///     An advisor that participates in the advice pipeline with eleven arguments.
/// </summary>
/// <typeparam name="T1">The type of the first argument.</typeparam>
/// <typeparam name="T2">The type of the second argument.</typeparam>
/// <typeparam name="T3">The type of the third argument.</typeparam>
/// <typeparam name="T4">The type of the fourth argument.</typeparam>
/// <typeparam name="T5">The type of the fifth argument.</typeparam>
/// <typeparam name="T6">The type of the sixth argument.</typeparam>
/// <typeparam name="T7">The type of the seventh argument.</typeparam>
/// <typeparam name="T8">The type of the eighth argument.</typeparam>
/// <typeparam name="T9">The type of the ninth argument.</typeparam>
/// <typeparam name="T10">The type of the tenth argument.</typeparam>
/// <typeparam name="T11">The type of the eleventh argument.</typeparam>
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

/// <summary>
///     An advisor that participates in the advice pipeline with twelve arguments.
/// </summary>
/// <typeparam name="T1">The type of the first argument.</typeparam>
/// <typeparam name="T2">The type of the second argument.</typeparam>
/// <typeparam name="T3">The type of the third argument.</typeparam>
/// <typeparam name="T4">The type of the fourth argument.</typeparam>
/// <typeparam name="T5">The type of the fifth argument.</typeparam>
/// <typeparam name="T6">The type of the sixth argument.</typeparam>
/// <typeparam name="T7">The type of the seventh argument.</typeparam>
/// <typeparam name="T8">The type of the eighth argument.</typeparam>
/// <typeparam name="T9">The type of the ninth argument.</typeparam>
/// <typeparam name="T10">The type of the tenth argument.</typeparam>
/// <typeparam name="T11">The type of the eleventh argument.</typeparam>
/// <typeparam name="T12">The type of the twelfth argument.</typeparam>
public interface IAdvisor<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12> : IAdvisor
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

/// <summary>
///     An advisor that participates in the advice pipeline with thirteen arguments.
/// </summary>
/// <typeparam name="T1">The type of the first argument.</typeparam>
/// <typeparam name="T2">The type of the second argument.</typeparam>
/// <typeparam name="T3">The type of the third argument.</typeparam>
/// <typeparam name="T4">The type of the fourth argument.</typeparam>
/// <typeparam name="T5">The type of the fifth argument.</typeparam>
/// <typeparam name="T6">The type of the sixth argument.</typeparam>
/// <typeparam name="T7">The type of the seventh argument.</typeparam>
/// <typeparam name="T8">The type of the eighth argument.</typeparam>
/// <typeparam name="T9">The type of the ninth argument.</typeparam>
/// <typeparam name="T10">The type of the tenth argument.</typeparam>
/// <typeparam name="T11">The type of the eleventh argument.</typeparam>
/// <typeparam name="T12">The type of the twelfth argument.</typeparam>
/// <typeparam name="T13">The type of the thirteenth argument.</typeparam>
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

/// <summary>
///     An advisor that participates in the advice pipeline with fourteen arguments.
/// </summary>
/// <typeparam name="T1">The type of the first argument.</typeparam>
/// <typeparam name="T2">The type of the second argument.</typeparam>
/// <typeparam name="T3">The type of the third argument.</typeparam>
/// <typeparam name="T4">The type of the fourth argument.</typeparam>
/// <typeparam name="T5">The type of the fifth argument.</typeparam>
/// <typeparam name="T6">The type of the sixth argument.</typeparam>
/// <typeparam name="T7">The type of the seventh argument.</typeparam>
/// <typeparam name="T8">The type of the eighth argument.</typeparam>
/// <typeparam name="T9">The type of the ninth argument.</typeparam>
/// <typeparam name="T10">The type of the tenth argument.</typeparam>
/// <typeparam name="T11">The type of the eleventh argument.</typeparam>
/// <typeparam name="T12">The type of the twelfth argument.</typeparam>
/// <typeparam name="T13">The type of the thirteenth argument.</typeparam>
/// <typeparam name="T14">The type of the fourteenth argument.</typeparam>
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

/// <summary>
///     An advisor that participates in the advice pipeline with fifteen arguments.
/// </summary>
/// <typeparam name="T1">The type of the first argument.</typeparam>
/// <typeparam name="T2">The type of the second argument.</typeparam>
/// <typeparam name="T3">The type of the third argument.</typeparam>
/// <typeparam name="T4">The type of the fourth argument.</typeparam>
/// <typeparam name="T5">The type of the fifth argument.</typeparam>
/// <typeparam name="T6">The type of the sixth argument.</typeparam>
/// <typeparam name="T7">The type of the seventh argument.</typeparam>
/// <typeparam name="T8">The type of the eighth argument.</typeparam>
/// <typeparam name="T9">The type of the ninth argument.</typeparam>
/// <typeparam name="T10">The type of the tenth argument.</typeparam>
/// <typeparam name="T11">The type of the eleventh argument.</typeparam>
/// <typeparam name="T12">The type of the twelfth argument.</typeparam>
/// <typeparam name="T13">The type of the thirteenth argument.</typeparam>
/// <typeparam name="T14">The type of the fourteenth argument.</typeparam>
/// <typeparam name="T15">The type of the fifteenth argument.</typeparam>
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

/// <summary>
///     An advisor that participates in the advice pipeline with sixteen arguments.
/// </summary>
/// <typeparam name="T1">The type of the first argument.</typeparam>
/// <typeparam name="T2">The type of the second argument.</typeparam>
/// <typeparam name="T3">The type of the third argument.</typeparam>
/// <typeparam name="T4">The type of the fourth argument.</typeparam>
/// <typeparam name="T5">The type of the fifth argument.</typeparam>
/// <typeparam name="T6">The type of the sixth argument.</typeparam>
/// <typeparam name="T7">The type of the seventh argument.</typeparam>
/// <typeparam name="T8">The type of the eighth argument.</typeparam>
/// <typeparam name="T9">The type of the ninth argument.</typeparam>
/// <typeparam name="T10">The type of the tenth argument.</typeparam>
/// <typeparam name="T11">The type of the eleventh argument.</typeparam>
/// <typeparam name="T12">The type of the twelfth argument.</typeparam>
/// <typeparam name="T13">The type of the thirteenth argument.</typeparam>
/// <typeparam name="T14">The type of the fourteenth argument.</typeparam>
/// <typeparam name="T15">The type of the fifteenth argument.</typeparam>
/// <typeparam name="T16">The type of the sixteenth argument.</typeparam>
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
