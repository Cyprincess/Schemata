using System;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;

namespace Schemata.Resource.Foundation.Internal;

internal static class ResourcePipelineRunner<TVerb>
{
    public static async Task<TResult?> RunAsync<TResult>(
        AdviceContext            ctx,
        Func<Task<AdviseResult>> advise,
        Func<Exception>          blocked,
        Func<TResult>?           handled = null
    ) where TResult : class {
        switch (await advise()) {
            case AdviseResult.Continue:
                return null;
            case AdviseResult.Handle when ctx.TryGet<TResult>(out var result):
                return result!;
            case AdviseResult.Handle when handled is not null:
                return handled();
            case AdviseResult.Block:
            default:
                throw blocked();
        }
    }
}
