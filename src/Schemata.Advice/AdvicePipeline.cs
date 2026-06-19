using Schemata.Abstractions.Advisors;

namespace Schemata.Advice;

/// <summary>
///     Carries the advisor type used by source-generated <c>RunAsync</c> extension methods.
/// </summary>
/// <typeparam name="TAdvisor">The advisor interface type this pipeline token targets.</typeparam>
public readonly struct AdvicePipeline<TAdvisor>
    where TAdvisor : IAdvisor;
