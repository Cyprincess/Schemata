using Schemata.Abstractions.Advisors;

namespace Schemata.Advice;

public static class Advisor
{
    public static AdvicePipeline<TAdvisor> For<TAdvisor>()
        where TAdvisor : IAdvisor {
        return default;
    }
}
