using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;

namespace Schemata.Advice;

/// <summary>
///     Short-circuit chain-of-responsibility runner for a single-argument
///     advisor. Resolves all DI-registered <typeparamref name="TAdvisor" />
///     implementations, sorts them by <see cref="IAdvisor.Order" />, invokes
///     each in turn, and returns the first non-<see cref="AdviseResult.Continue" />
///     result.
/// </summary>
/// <typeparam name="TAdvisor">An advisor interface implementing <see cref="IAdvisor{T1}" />.</typeparam>
/// <typeparam name="T1">The type of the argument passed to each advisor.</typeparam>
public static class AdviceRunner<TAdvisor, T1>
    where TAdvisor : IAdvisor<T1>
{
    /// <summary>
    ///     Executes the advisor pipeline for <typeparamref name="TAdvisor" />,
    ///     resolving all implementations from the current service scope and
    ///     invoking them in <see cref="IAdvisor.Order" /> order. The chain
    ///     short-circuits on the first non-<see cref="AdviseResult.Continue" />
    ///     result and returns it immediately.
    /// </summary>
    /// <param name="ctx">The <see cref="AdviceContext" /> providing the service scope and shared state.</param>
    /// <param name="a1">The pipeline argument.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    ///     The first non-<see cref="AdviseResult.Continue" /> result, or
    ///     <see cref="AdviseResult.Continue" /> if all advisors continue.
    /// </returns>
    public static async Task<AdviseResult> RunAsync(AdviceContext ctx, T1 a1, CancellationToken ct = default) {
        var advisors = ctx.ServiceProvider.GetServices<TAdvisor>().OrderBy(a => a.Order).ToList();
        foreach (var advisor in advisors) {
            ct.ThrowIfCancellationRequested();
            var result = await advisor.AdviseAsync(ctx, a1, ct);
            if (result is not AdviseResult.Continue) {
                return result;
            }
        }

        return AdviseResult.Continue;
    }
}
