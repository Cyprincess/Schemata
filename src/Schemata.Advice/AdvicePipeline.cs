using Schemata.Abstractions.Advisors;

namespace Schemata.Advice;

/// <summary>
///     A zero-size struct used as a type token for dispatch of source-generated <c>RunAsync</c> extension methods.
/// </summary>
/// <typeparam name="TAdvisor">The advisor interface type this pipeline runs.</typeparam>
public readonly struct AdvicePipeline<TAdvisor>
    where TAdvisor : IAdvisor;
