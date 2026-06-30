using System;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>Describes a source binding referenced by a condition expression.</summary>
public interface ISourceCondition
{
    /// <summary>The source binding name.</summary>
    string? Name { get; }

    /// <summary>The bound source entity type.</summary>
    Type SourceType { get; }
}
