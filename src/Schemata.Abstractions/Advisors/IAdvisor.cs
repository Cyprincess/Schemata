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

/// <summary>
///     Typed advisor receiving two arguments.
/// </summary>
/// <typeparam name="T1">The first argument type.</typeparam>
/// <typeparam name="T2">The second argument type.</typeparam>
public interface IAdvisor<in T1, in T2> : IAdvisor
{
    /// <summary>
    ///     Evaluates the advise request and returns a result controlling the pipeline.
    /// </summary>
    /// <param name="ctx">The shared <see cref="AdviceContext" /> flowing through the pipeline.</param>
    /// <param name="a1">The first argument.</param>
    /// <param name="a2">The second argument.</param>
    /// <param name="ct">A cancellation token.</param>
    Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        CancellationToken ct = default
    );
}

/// <summary>
///     Typed advisor receiving three arguments.
/// </summary>
/// <typeparam name="T1">The first argument type.</typeparam>
/// <typeparam name="T2">The second argument type.</typeparam>
/// <typeparam name="T3">The third argument type.</typeparam>
public interface IAdvisor<in T1, in T2, in T3> : IAdvisor
{
    /// <summary>
    ///     Evaluates the advise request and returns a result controlling the pipeline.
    /// </summary>
    /// <param name="ctx">The shared <see cref="AdviceContext" /> flowing through the pipeline.</param>
    /// <param name="a1">The first argument.</param>
    /// <param name="a2">The second argument.</param>
    /// <param name="a3">The third argument.</param>
    /// <param name="ct">A cancellation token.</param>
    Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        T3                a3,
        CancellationToken ct = default
    );
}

/// <summary>
///     Typed advisor receiving four arguments.
/// </summary>
/// <typeparam name="T1">The first argument type.</typeparam>
/// <typeparam name="T2">The second argument type.</typeparam>
/// <typeparam name="T3">The third argument type.</typeparam>
/// <typeparam name="T4">The fourth argument type.</typeparam>
public interface IAdvisor<in T1, in T2, in T3, in T4> : IAdvisor
{
    /// <summary>
    ///     Evaluates the advise request and returns a result controlling the pipeline.
    /// </summary>
    /// <param name="ctx">The shared <see cref="AdviceContext" /> flowing through the pipeline.</param>
    /// <param name="a1">The first argument.</param>
    /// <param name="a2">The second argument.</param>
    /// <param name="a3">The third argument.</param>
    /// <param name="a4">The fourth argument.</param>
    /// <param name="ct">A cancellation token.</param>
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
///     Typed advisor receiving five arguments.
/// </summary>
/// <typeparam name="T1">The first argument type.</typeparam>
/// <typeparam name="T2">The second argument type.</typeparam>
/// <typeparam name="T3">The third argument type.</typeparam>
/// <typeparam name="T4">The fourth argument type.</typeparam>
/// <typeparam name="T5">The fifth argument type.</typeparam>
public interface IAdvisor<in T1, in T2, in T3, in T4, in T5> : IAdvisor
{
    /// <summary>
    ///     Evaluates the advise request and returns a result controlling the pipeline.
    /// </summary>
    /// <param name="ctx">The shared <see cref="AdviceContext" /> flowing through the pipeline.</param>
    /// <param name="a1">The first argument.</param>
    /// <param name="a2">The second argument.</param>
    /// <param name="a3">The third argument.</param>
    /// <param name="a4">The fourth argument.</param>
    /// <param name="a5">The fifth argument.</param>
    /// <param name="ct">A cancellation token.</param>
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
///     Typed advisor receiving six arguments.
/// </summary>
/// <typeparam name="T1">The first argument type.</typeparam>
/// <typeparam name="T2">The second argument type.</typeparam>
/// <typeparam name="T3">The third argument type.</typeparam>
/// <typeparam name="T4">The fourth argument type.</typeparam>
/// <typeparam name="T5">The fifth argument type.</typeparam>
/// <typeparam name="T6">The sixth argument type.</typeparam>
public interface IAdvisor<in T1, in T2, in T3, in T4, in T5, in T6> : IAdvisor
{
    /// <summary>
    ///     Evaluates the advise request and returns a result controlling the pipeline.
    /// </summary>
    /// <param name="ctx">The shared <see cref="AdviceContext" /> flowing through the pipeline.</param>
    /// <param name="a1">The first argument.</param>
    /// <param name="a2">The second argument.</param>
    /// <param name="a3">The third argument.</param>
    /// <param name="a4">The fourth argument.</param>
    /// <param name="a5">The fifth argument.</param>
    /// <param name="a6">The sixth argument.</param>
    /// <param name="ct">A cancellation token.</param>
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
///     Typed advisor receiving seven arguments.
/// </summary>
/// <typeparam name="T1">The first argument type.</typeparam>
/// <typeparam name="T2">The second argument type.</typeparam>
/// <typeparam name="T3">The third argument type.</typeparam>
/// <typeparam name="T4">The fourth argument type.</typeparam>
/// <typeparam name="T5">The fifth argument type.</typeparam>
/// <typeparam name="T6">The sixth argument type.</typeparam>
/// <typeparam name="T7">The seventh argument type.</typeparam>
public interface IAdvisor<in T1, in T2, in T3, in T4, in T5, in T6, in T7> : IAdvisor
{
    /// <summary>
    ///     Evaluates the advise request and returns a result controlling the pipeline.
    /// </summary>
    /// <param name="ctx">The shared <see cref="AdviceContext" /> flowing through the pipeline.</param>
    /// <param name="a1">The first argument.</param>
    /// <param name="a2">The second argument.</param>
    /// <param name="a3">The third argument.</param>
    /// <param name="a4">The fourth argument.</param>
    /// <param name="a5">The fifth argument.</param>
    /// <param name="a6">The sixth argument.</param>
    /// <param name="a7">The seventh argument.</param>
    /// <param name="ct">A cancellation token.</param>
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
///     Typed advisor receiving eight arguments.
/// </summary>
/// <typeparam name="T1">The first argument type.</typeparam>
/// <typeparam name="T2">The second argument type.</typeparam>
/// <typeparam name="T3">The third argument type.</typeparam>
/// <typeparam name="T4">The fourth argument type.</typeparam>
/// <typeparam name="T5">The fifth argument type.</typeparam>
/// <typeparam name="T6">The sixth argument type.</typeparam>
/// <typeparam name="T7">The seventh argument type.</typeparam>
/// <typeparam name="T8">The eighth argument type.</typeparam>
public interface IAdvisor<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8> : IAdvisor
{
    /// <summary>
    ///     Evaluates the advise request and returns a result controlling the pipeline.
    /// </summary>
    /// <param name="ctx">The shared <see cref="AdviceContext" /> flowing through the pipeline.</param>
    /// <param name="a1">The first argument.</param>
    /// <param name="a2">The second argument.</param>
    /// <param name="a3">The third argument.</param>
    /// <param name="a4">The fourth argument.</param>
    /// <param name="a5">The fifth argument.</param>
    /// <param name="a6">The sixth argument.</param>
    /// <param name="a7">The seventh argument.</param>
    /// <param name="a8">The eighth argument.</param>
    /// <param name="ct">A cancellation token.</param>
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
///     Typed advisor receiving nine arguments.
/// </summary>
/// <typeparam name="T1">The first argument type.</typeparam>
/// <typeparam name="T2">The second argument type.</typeparam>
/// <typeparam name="T3">The third argument type.</typeparam>
/// <typeparam name="T4">The fourth argument type.</typeparam>
/// <typeparam name="T5">The fifth argument type.</typeparam>
/// <typeparam name="T6">The sixth argument type.</typeparam>
/// <typeparam name="T7">The seventh argument type.</typeparam>
/// <typeparam name="T8">The eighth argument type.</typeparam>
/// <typeparam name="T9">The ninth argument type.</typeparam>
public interface IAdvisor<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9> : IAdvisor
{
    /// <summary>
    ///     Evaluates the advise request and returns a result controlling the pipeline.
    /// </summary>
    /// <param name="ctx">The shared <see cref="AdviceContext" /> flowing through the pipeline.</param>
    /// <param name="a1">The first argument.</param>
    /// <param name="a2">The second argument.</param>
    /// <param name="a3">The third argument.</param>
    /// <param name="a4">The fourth argument.</param>
    /// <param name="a5">The fifth argument.</param>
    /// <param name="a6">The sixth argument.</param>
    /// <param name="a7">The seventh argument.</param>
    /// <param name="a8">The eighth argument.</param>
    /// <param name="a9">The ninth argument.</param>
    /// <param name="ct">A cancellation token.</param>
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
///     Typed advisor receiving ten arguments.
/// </summary>
/// <typeparam name="T1">The first argument type.</typeparam>
/// <typeparam name="T2">The second argument type.</typeparam>
/// <typeparam name="T3">The third argument type.</typeparam>
/// <typeparam name="T4">The fourth argument type.</typeparam>
/// <typeparam name="T5">The fifth argument type.</typeparam>
/// <typeparam name="T6">The sixth argument type.</typeparam>
/// <typeparam name="T7">The seventh argument type.</typeparam>
/// <typeparam name="T8">The eighth argument type.</typeparam>
/// <typeparam name="T9">The ninth argument type.</typeparam>
/// <typeparam name="T10">The tenth argument type.</typeparam>
public interface IAdvisor<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10> : IAdvisor
{
    /// <summary>
    ///     Evaluates the advise request and returns a result controlling the pipeline.
    /// </summary>
    /// <param name="ctx">The shared <see cref="AdviceContext" /> flowing through the pipeline.</param>
    /// <param name="a1">The first argument.</param>
    /// <param name="a2">The second argument.</param>
    /// <param name="a3">The third argument.</param>
    /// <param name="a4">The fourth argument.</param>
    /// <param name="a5">The fifth argument.</param>
    /// <param name="a6">The sixth argument.</param>
    /// <param name="a7">The seventh argument.</param>
    /// <param name="a8">The eighth argument.</param>
    /// <param name="a9">The ninth argument.</param>
    /// <param name="a10">The tenth argument.</param>
    /// <param name="ct">A cancellation token.</param>
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
///     Typed advisor receiving eleven arguments.
/// </summary>
/// <typeparam name="T1">The first argument type.</typeparam>
/// <typeparam name="T2">The second argument type.</typeparam>
/// <typeparam name="T3">The third argument type.</typeparam>
/// <typeparam name="T4">The fourth argument type.</typeparam>
/// <typeparam name="T5">The fifth argument type.</typeparam>
/// <typeparam name="T6">The sixth argument type.</typeparam>
/// <typeparam name="T7">The seventh argument type.</typeparam>
/// <typeparam name="T8">The eighth argument type.</typeparam>
/// <typeparam name="T9">The ninth argument type.</typeparam>
/// <typeparam name="T10">The tenth argument type.</typeparam>
/// <typeparam name="T11">The eleventh argument type.</typeparam>
public interface IAdvisor<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11> : IAdvisor
{
    /// <summary>
    ///     Evaluates the advise request and returns a result controlling the pipeline.
    /// </summary>
    /// <param name="ctx">The shared <see cref="AdviceContext" /> flowing through the pipeline.</param>
    /// <param name="a1">The first argument.</param>
    /// <param name="a2">The second argument.</param>
    /// <param name="a3">The third argument.</param>
    /// <param name="a4">The fourth argument.</param>
    /// <param name="a5">The fifth argument.</param>
    /// <param name="a6">The sixth argument.</param>
    /// <param name="a7">The seventh argument.</param>
    /// <param name="a8">The eighth argument.</param>
    /// <param name="a9">The ninth argument.</param>
    /// <param name="a10">The tenth argument.</param>
    /// <param name="a11">The eleventh argument.</param>
    /// <param name="ct">A cancellation token.</param>
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
///     Typed advisor receiving twelve arguments.
/// </summary>
/// <typeparam name="T1">The first argument type.</typeparam>
/// <typeparam name="T2">The second argument type.</typeparam>
/// <typeparam name="T3">The third argument type.</typeparam>
/// <typeparam name="T4">The fourth argument type.</typeparam>
/// <typeparam name="T5">The fifth argument type.</typeparam>
/// <typeparam name="T6">The sixth argument type.</typeparam>
/// <typeparam name="T7">The seventh argument type.</typeparam>
/// <typeparam name="T8">The eighth argument type.</typeparam>
/// <typeparam name="T9">The ninth argument type.</typeparam>
/// <typeparam name="T10">The tenth argument type.</typeparam>
/// <typeparam name="T11">The eleventh argument type.</typeparam>
/// <typeparam name="T12">The twelfth argument type.</typeparam>
public interface
    IAdvisor<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12> : IAdvisor
{
    /// <summary>
    ///     Evaluates the advise request and returns a result controlling the pipeline.
    /// </summary>
    /// <param name="ctx">The shared <see cref="AdviceContext" /> flowing through the pipeline.</param>
    /// <param name="a1">The first argument.</param>
    /// <param name="a2">The second argument.</param>
    /// <param name="a3">The third argument.</param>
    /// <param name="a4">The fourth argument.</param>
    /// <param name="a5">The fifth argument.</param>
    /// <param name="a6">The sixth argument.</param>
    /// <param name="a7">The seventh argument.</param>
    /// <param name="a8">The eighth argument.</param>
    /// <param name="a9">The ninth argument.</param>
    /// <param name="a10">The tenth argument.</param>
    /// <param name="a11">The eleventh argument.</param>
    /// <param name="a12">The twelfth argument.</param>
    /// <param name="ct">A cancellation token.</param>
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
///     Typed advisor receiving thirteen arguments.
/// </summary>
/// <typeparam name="T1">The first argument type.</typeparam>
/// <typeparam name="T2">The second argument type.</typeparam>
/// <typeparam name="T3">The third argument type.</typeparam>
/// <typeparam name="T4">The fourth argument type.</typeparam>
/// <typeparam name="T5">The fifth argument type.</typeparam>
/// <typeparam name="T6">The sixth argument type.</typeparam>
/// <typeparam name="T7">The seventh argument type.</typeparam>
/// <typeparam name="T8">The eighth argument type.</typeparam>
/// <typeparam name="T9">The ninth argument type.</typeparam>
/// <typeparam name="T10">The tenth argument type.</typeparam>
/// <typeparam name="T11">The eleventh argument type.</typeparam>
/// <typeparam name="T12">The twelfth argument type.</typeparam>
/// <typeparam name="T13">The thirteenth argument type.</typeparam>
public interface IAdvisor<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13> : IAdvisor
{
    /// <summary>
    ///     Evaluates the advise request and returns a result controlling the pipeline.
    /// </summary>
    /// <param name="ctx">The shared <see cref="AdviceContext" /> flowing through the pipeline.</param>
    /// <param name="a1">The first argument.</param>
    /// <param name="a2">The second argument.</param>
    /// <param name="a3">The third argument.</param>
    /// <param name="a4">The fourth argument.</param>
    /// <param name="a5">The fifth argument.</param>
    /// <param name="a6">The sixth argument.</param>
    /// <param name="a7">The seventh argument.</param>
    /// <param name="a8">The eighth argument.</param>
    /// <param name="a9">The ninth argument.</param>
    /// <param name="a10">The tenth argument.</param>
    /// <param name="a11">The eleventh argument.</param>
    /// <param name="a12">The twelfth argument.</param>
    /// <param name="a13">The thirteenth argument.</param>
    /// <param name="ct">A cancellation token.</param>
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
///     Typed advisor receiving fourteen arguments.
/// </summary>
/// <typeparam name="T1">The first argument type.</typeparam>
/// <typeparam name="T2">The second argument type.</typeparam>
/// <typeparam name="T3">The third argument type.</typeparam>
/// <typeparam name="T4">The fourth argument type.</typeparam>
/// <typeparam name="T5">The fifth argument type.</typeparam>
/// <typeparam name="T6">The sixth argument type.</typeparam>
/// <typeparam name="T7">The seventh argument type.</typeparam>
/// <typeparam name="T8">The eighth argument type.</typeparam>
/// <typeparam name="T9">The ninth argument type.</typeparam>
/// <typeparam name="T10">The tenth argument type.</typeparam>
/// <typeparam name="T11">The eleventh argument type.</typeparam>
/// <typeparam name="T12">The twelfth argument type.</typeparam>
/// <typeparam name="T13">The thirteenth argument type.</typeparam>
/// <typeparam name="T14">The fourteenth argument type.</typeparam>
public interface IAdvisor<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, in T14> : IAdvisor
{
    /// <summary>
    ///     Evaluates the advise request and returns a result controlling the pipeline.
    /// </summary>
    /// <param name="ctx">The shared <see cref="AdviceContext" /> flowing through the pipeline.</param>
    /// <param name="a1">The first argument.</param>
    /// <param name="a2">The second argument.</param>
    /// <param name="a3">The third argument.</param>
    /// <param name="a4">The fourth argument.</param>
    /// <param name="a5">The fifth argument.</param>
    /// <param name="a6">The sixth argument.</param>
    /// <param name="a7">The seventh argument.</param>
    /// <param name="a8">The eighth argument.</param>
    /// <param name="a9">The ninth argument.</param>
    /// <param name="a10">The tenth argument.</param>
    /// <param name="a11">The eleventh argument.</param>
    /// <param name="a12">The twelfth argument.</param>
    /// <param name="a13">The thirteenth argument.</param>
    /// <param name="a14">The fourteenth argument.</param>
    /// <param name="ct">A cancellation token.</param>
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
///     Typed advisor receiving fifteen arguments.
/// </summary>
/// <typeparam name="T1">The first argument type.</typeparam>
/// <typeparam name="T2">The second argument type.</typeparam>
/// <typeparam name="T3">The third argument type.</typeparam>
/// <typeparam name="T4">The fourth argument type.</typeparam>
/// <typeparam name="T5">The fifth argument type.</typeparam>
/// <typeparam name="T6">The sixth argument type.</typeparam>
/// <typeparam name="T7">The seventh argument type.</typeparam>
/// <typeparam name="T8">The eighth argument type.</typeparam>
/// <typeparam name="T9">The ninth argument type.</typeparam>
/// <typeparam name="T10">The tenth argument type.</typeparam>
/// <typeparam name="T11">The eleventh argument type.</typeparam>
/// <typeparam name="T12">The twelfth argument type.</typeparam>
/// <typeparam name="T13">The thirteenth argument type.</typeparam>
/// <typeparam name="T14">The fourteenth argument type.</typeparam>
/// <typeparam name="T15">The fifteenth argument type.</typeparam>
public interface IAdvisor<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, in T14, in T15> : IAdvisor
{
    /// <summary>
    ///     Evaluates the advise request and returns a result controlling the pipeline.
    /// </summary>
    /// <param name="ctx">The shared <see cref="AdviceContext" /> flowing through the pipeline.</param>
    /// <param name="a1">The first argument.</param>
    /// <param name="a2">The second argument.</param>
    /// <param name="a3">The third argument.</param>
    /// <param name="a4">The fourth argument.</param>
    /// <param name="a5">The fifth argument.</param>
    /// <param name="a6">The sixth argument.</param>
    /// <param name="a7">The seventh argument.</param>
    /// <param name="a8">The eighth argument.</param>
    /// <param name="a9">The ninth argument.</param>
    /// <param name="a10">The tenth argument.</param>
    /// <param name="a11">The eleventh argument.</param>
    /// <param name="a12">The twelfth argument.</param>
    /// <param name="a13">The thirteenth argument.</param>
    /// <param name="a14">The fourteenth argument.</param>
    /// <param name="a15">The fifteenth argument.</param>
    /// <param name="ct">A cancellation token.</param>
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
///     Typed advisor receiving sixteen arguments.
/// </summary>
/// <typeparam name="T1">The first argument type.</typeparam>
/// <typeparam name="T2">The second argument type.</typeparam>
/// <typeparam name="T3">The third argument type.</typeparam>
/// <typeparam name="T4">The fourth argument type.</typeparam>
/// <typeparam name="T5">The fifth argument type.</typeparam>
/// <typeparam name="T6">The sixth argument type.</typeparam>
/// <typeparam name="T7">The seventh argument type.</typeparam>
/// <typeparam name="T8">The eighth argument type.</typeparam>
/// <typeparam name="T9">The ninth argument type.</typeparam>
/// <typeparam name="T10">The tenth argument type.</typeparam>
/// <typeparam name="T11">The eleventh argument type.</typeparam>
/// <typeparam name="T12">The twelfth argument type.</typeparam>
/// <typeparam name="T13">The thirteenth argument type.</typeparam>
/// <typeparam name="T14">The fourteenth argument type.</typeparam>
/// <typeparam name="T15">The fifteenth argument type.</typeparam>
/// <typeparam name="T16">The sixteenth argument type.</typeparam>
public interface IAdvisor<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, in T14, in T15, in T16> : IAdvisor
{
    /// <summary>
    ///     Evaluates the advise request and returns a result controlling the pipeline.
    /// </summary>
    /// <param name="ctx">The shared <see cref="AdviceContext" /> flowing through the pipeline.</param>
    /// <param name="a1">The first argument.</param>
    /// <param name="a2">The second argument.</param>
    /// <param name="a3">The third argument.</param>
    /// <param name="a4">The fourth argument.</param>
    /// <param name="a5">The fifth argument.</param>
    /// <param name="a6">The sixth argument.</param>
    /// <param name="a7">The seventh argument.</param>
    /// <param name="a8">The eighth argument.</param>
    /// <param name="a9">The ninth argument.</param>
    /// <param name="a10">The tenth argument.</param>
    /// <param name="a11">The eleventh argument.</param>
    /// <param name="a12">The twelfth argument.</param>
    /// <param name="a13">The thirteenth argument.</param>
    /// <param name="a14">The fourteenth argument.</param>
    /// <param name="a15">The fifteenth argument.</param>
    /// <param name="a16">The sixteenth argument.</param>
    /// <param name="ct">A cancellation token.</param>
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
