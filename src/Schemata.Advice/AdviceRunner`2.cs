using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;

namespace Schemata.Advice;

/// <summary>
///     Executes all registered advisors of type <typeparamref name="TAdvisor" /> in order, stopping on non-Continue
///     results.
/// </summary>
/// <typeparam name="TAdvisor">The advisor interface type.</typeparam>
/// <typeparam name="T1">The type of the first argument.</typeparam>
public static class AdviceRunner<TAdvisor, T1>
    where TAdvisor : IAdvisor<T1>
{
    /// <summary>
    ///     Runs the advisor pipeline.
    /// </summary>
    /// <param name="ctx">The advice context.</param>
    /// <param name="a1">The first argument.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The pipeline result.</returns>
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
