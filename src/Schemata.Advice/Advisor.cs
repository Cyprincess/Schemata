using Schemata.Abstractions.Advisors;

namespace Schemata.Advice;

/// <summary>
///     Creates advisor pipeline tokens for source-generated <c>RunAsync</c> extension methods.
/// </summary>
public static class Advisor
{
    /// <summary>
    ///     Returns a pipeline token for source-generated <c>RunAsync</c> extension methods.
    /// </summary>
    /// <typeparam name="TAdvisor">The advisor interface type that carries the advice contract and arguments.</typeparam>
    /// <returns>The pipeline token for <typeparamref name="TAdvisor" />.</returns>
    public static AdvicePipeline<TAdvisor> For<TAdvisor>()
        where TAdvisor : IAdvisor {
        return default;
    }
}
