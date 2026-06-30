using System;
using System.Collections.Generic;
using Schemata.Entity.Repository;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>
///     Transactional services supplied by the Flow handler to a runtime engine invocation.
/// </summary>
public sealed class FlowExecutionContext
{
    /// <summary>Initializes a flow execution context.</summary>
    /// <param name="unitOfWork">The unit of work shared by process, token, transition, source, and user repositories.</param>
    /// <param name="services">The scoped service provider used to resolve repositories and advisors.</param>
    public FlowExecutionContext(IUnitOfWork unitOfWork, IServiceProvider services) {
        UnitOfWork = unitOfWork;
        Services   = services;
    }

    /// <summary>The unit of work shared by every repository enlisted during this engine call.</summary>
    public IUnitOfWork UnitOfWork { get; }

    /// <summary>The scoped service provider used to resolve repositories and advisors.</summary>
    public IServiceProvider Services { get; }

    internal IDictionary<(Type SourceType, string CanonicalName), object> TouchedSources { get; } = new Dictionary<(Type SourceType, string CanonicalName), object>();
}
