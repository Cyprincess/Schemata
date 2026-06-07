using Schemata.Abstractions.Advisors;

namespace Schemata.Advice;

/// <summary>
///     Zero-size struct that carries the generic parameter
///     <typeparamref name="TAdvisor" /> so source-generated <c>RunAsync</c>
///     extension methods dispatch to the correct
///     <see cref="AdviceRunner{TAdvisor, T1}" /> overload at zero heap cost.
/// </summary>
/// <typeparam name="TAdvisor">The advisor interface type this pipeline token targets.</typeparam>
public readonly struct AdvicePipeline<TAdvisor>
    where TAdvisor : IAdvisor;
