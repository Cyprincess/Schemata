using Schemata.Abstractions.Advisors;

namespace Schemata.Advice;

/// <summary>
///     Entry point for creating type-safe advisor pipelines.
/// </summary>
public static class Advisor
{
    /// <summary>
    ///     Creates an <see cref="AdvicePipeline{TAdvisor}" /> token for the specified advisor type, enabling extension method dispatch.
    /// </summary>
    /// <typeparam name="TAdvisor">The advisor interface type.</typeparam>
    /// <returns>A pipeline token used to invoke <c>RunAsync</c> extension methods.</returns>
    public static AdvicePipeline<TAdvisor> For<TAdvisor>()
        where TAdvisor : IAdvisor {
        return default;
    }
}
