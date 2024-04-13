using System;
using System.Linq.Expressions;

namespace Schemata.Mapping.Skeleton.Configurations;

public interface IMapping
{
    Type SourceType { get; }

    Type DestinationType { get; }

    bool IsConverter { get; }

    bool IsIgnored { get; }

    bool HasSourceField { get; }

    string DestinationFieldName { get; }

    void Invoke(Action<Expression?, Expression?, Expression?, Expression?, bool> action);
}
