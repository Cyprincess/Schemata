using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;

namespace Schemata.Advice;

public static class AdviceRunner<TAdvisor, T1, T2, T3>
    where TAdvisor : IAdvisor<T1, T2, T3>
{
    public static async Task<AdviseResult> RunAsync(
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        T3                a3,
        CancellationToken ct = default
    ) {
        var advisors = ctx.ServiceProvider.GetServices<TAdvisor>().OrderBy(a => a.Order).ToList();
        foreach (var advisor in advisors) {
            ct.ThrowIfCancellationRequested();
            var result = await advisor.AdviseAsync(ctx, a1, a2, a3, ct);
            if (result is not AdviseResult.Continue) {
                return result;
            }
        }

        return AdviseResult.Continue;
    }
}
