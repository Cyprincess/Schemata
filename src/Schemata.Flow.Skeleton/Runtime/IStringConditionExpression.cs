using System;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>Registration-time view of a string condition awaiting compilation.</summary>
internal interface IStringConditionExpression
{
    /// <summary>The expression source text.</summary>
    string Expression { get; }

    /// <summary>The bound source entity type the predicate is compiled against.</summary>
    Type SourceType { get; }

    /// <summary>Whether a compiled predicate has been bound.</summary>
    bool Compiled { get; }

    /// <summary>Binds the compiled <c>Func&lt;TSource, bool&gt;</c> predicate.</summary>
    void Bind(Delegate predicate);
}
