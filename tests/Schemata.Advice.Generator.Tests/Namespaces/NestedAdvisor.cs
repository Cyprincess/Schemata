using Schemata.Abstractions.Advisors;

namespace Schemata.Advice.Generator.Tests.Namespaces;

public class Outer<T> where T : class
{
    public interface IValidate<TValue> : IAdvisor<T, TValue> where TValue : struct;
}
