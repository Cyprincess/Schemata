using Schemata.Abstractions.Advisors;

namespace Schemata.Advice;

public readonly struct AdvicePipeline<TAdvisor>
    where TAdvisor : IAdvisor;
