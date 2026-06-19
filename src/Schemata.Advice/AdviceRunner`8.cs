using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;

namespace Schemata.Advice;

/// <summary>
///     Runs advisor pipelines that pass seven arguments to each advisor.
/// </summary>
/// <typeparam name="TAdvisor">An advisor interface implementing <see cref="IAdvisor{T1, T2, T3, T4, T5, T6, T7}" />.</typeparam>
/// <typeparam name="T1">The type of the first advisor argument.</typeparam>
/// <typeparam name="T2">The type of the second advisor argument.</typeparam>
/// <typeparam name="T3">The type of the third advisor argument.</typeparam>
/// <typeparam name="T4">The type of the fourth advisor argument.</typeparam>
/// <typeparam name="T5">The type of the fifth advisor argument.</typeparam>
/// <typeparam name="T6">The type of the sixth advisor argument.</typeparam>
/// <typeparam name="T7">The type of the seventh advisor argument.</typeparam>
public static class AdviceRunner<TAdvisor, T1, T2, T3, T4, T5, T6, T7>
    where TAdvisor : IAdvisor<T1, T2, T3, T4, T5, T6, T7>
{
    /// <summary>
    ///     Executes the advisor pipeline for <typeparamref name="TAdvisor" />,
    ///     resolving all implementations from the current service scope and
    ///     invoking them in <see cref="IAdvisor.Order" /> order. The chain
    ///     short-circuits on the first non-<see cref="AdviseResult.Continue" />
    ///     result and returns it immediately.
    /// </summary>
    /// <param name="ctx">The <see cref="AdviceContext" /> providing the service scope and shared state.</param>
    /// <param name="a1">The first argument.</param>
    /// <param name="a2">The second argument.</param>
    /// <param name="a3">The third argument.</param>
    /// <param name="a4">The fourth argument.</param>
    /// <param name="a5">The fifth argument.</param>
    /// <param name="a6">The sixth argument.</param>
    /// <param name="a7">The seventh argument.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    ///     The first non-<see cref="AdviseResult.Continue" /> result, or
    ///     <see cref="AdviseResult.Continue" /> if all advisors continue.
    /// </returns>
    public static async Task<AdviseResult> RunAsync(
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        T3                a3,
        T4                a4,
        T5                a5,
        T6                a6,
        T7                a7,
        CancellationToken ct = default
    ) {
        var advisors = ctx.ServiceProvider.GetServices<TAdvisor>().OrderBy(a => a.Order).ToList();
        foreach (var advisor in advisors) {
            ct.ThrowIfCancellationRequested();
            var result = await advisor.AdviseAsync(ctx, a1, a2, a3, a4, a5, a6, a7, ct);
            if (result is not AdviseResult.Continue) {
                return result;
            }
        }

        return AdviseResult.Continue;
    }
}
