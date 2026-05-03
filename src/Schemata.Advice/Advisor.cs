using Schemata.Abstractions.Advisors;

namespace Schemata.Advice;

/// <summary>
///     Entry point for the Advice pattern. Call <see cref="For{TAdvisor}" /> to obtain an
///     <see cref="AdvicePipeline{TAdvisor}" /> token, then chain any source-generated
///     <c>RunAsync</c> extension method to execute the advisor chain-of-responsibility.
/// </summary>
public static class Advisor
{
    /// <summary>
    ///     Returns the <see cref="AdvicePipeline{TAdvisor}" /> pipeline token for
    ///     <typeparamref name="TAdvisor" />. This is a zero-size struct whose sole
    ///     purpose is to carry the advisor type so that source-generated
    ///     <c>RunAsync</c> overloads resolve without heap allocation.
    /// </summary>
    /// <typeparam name="TAdvisor">The advisor interface type that carries the advice contract and arguments.</typeparam>
    /// <returns>A zero-size <see cref="AdvicePipeline{TAdvisor}" /> struct.</returns>
    public static AdvicePipeline<TAdvisor> For<TAdvisor>()
        where TAdvisor : IAdvisor {
        return default;
    }
}
