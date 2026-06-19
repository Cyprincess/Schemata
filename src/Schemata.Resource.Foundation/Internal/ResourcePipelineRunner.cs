using System;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;

namespace Schemata.Resource.Foundation.Internal;

/// <summary>
///     Runs a resource advisor stage and normalizes continue, handled, and blocked outcomes.
/// </summary>
/// <typeparam name="TVerb">The operation token type used by the owning pipeline.</typeparam>
internal static class ResourcePipelineRunner<TVerb>
{
    /// <summary>
    ///     Executes an advisor delegate and returns a handled result when the stage short-circuits.
    /// </summary>
    /// <typeparam name="TResult">The response type carried by handled advisor results.</typeparam>
    /// <param name="ctx">The advisor context for reading handled results.</param>
    /// <param name="advise">The advisor stage to execute.</param>
    /// <param name="blocked">Factory for the exception raised when the stage blocks.</param>
    /// <param name="handled">Fallback result factory for handled stages lacking a stashed result.</param>
    /// <returns>The handled result, or <see langword="null" /> when processing should continue.</returns>
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
