using Schemata.Abstractions.Advisors;

namespace Schemata.Advice;

/// <summary>
///     Zero-size struct serving as a pure type marker for the advisor pipeline
///     system. Its sole purpose is to carry the generic parameter
///     <typeparamref name="TAdvisor" /> so that source-generated <c>RunAsync</c>
///     extension methods dispatch to the correct
///     <see cref="AdviceRunner{TAdvisor, T1}" /> overload without any heap allocation.
/// </summary>
/// <typeparam name="TAdvisor">The advisor interface type this pipeline token targets.</typeparam>
public readonly struct AdvicePipeline<TAdvisor>
    where TAdvisor : IAdvisor;
