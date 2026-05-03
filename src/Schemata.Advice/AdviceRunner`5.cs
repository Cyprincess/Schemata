using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;

namespace Schemata.Advice;

/// <inheritdoc cref="AdviceRunner{TAdvisor, T1}" />
public static class AdviceRunner<TAdvisor, T1, T2, T3, T4>
    where TAdvisor : IAdvisor<T1, T2, T3, T4>
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
        CancellationToken ct = default
    ) {
        var advisors = ctx.ServiceProvider.GetServices<TAdvisor>().OrderBy(a => a.Order).ToList();
        foreach (var advisor in advisors) {
            ct.ThrowIfCancellationRequested();
            var result = await advisor.AdviseAsync(ctx, a1, a2, a3, a4, ct);
            if (result is not AdviseResult.Continue) {
                return result;
            }
        }

        return AdviseResult.Continue;
    }
}
