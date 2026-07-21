using System;
using System.Collections.Generic;
using System.Security.Claims;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Models;

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

    /// <summary>
    ///     The principal that initiated this engine operation, or <see langword="null" /> for
    ///     system-initiated continuations (timer and event bridges).
    /// </summary>
    public ClaimsPrincipal? Principal { get; init; }

    /// <summary>
    ///     Foundation-supplied guard suppressing visibility filters for reads of already-bound
    ///     source entities. Callers hold the returned scope for the duration of one read; the same
    ///     repository instance must not be guarded concurrently.
    /// </summary>
    public Func<IRepository, IDisposable>? SourceReadGuard { get; init; }

    /// <summary>Compensation handlers restored from persisted process state for this engine operation.</summary>
    public IReadOnlyList<ProcessCompensationBinding> LoadedCompensationBindings { get; init; } = [];

    internal List<ProcessCompensationBinding> CompensationBindings { get; } = [];

    internal bool CompensationBindingsLoaded { get; set; }

    internal IDictionary<(Type SourceType, string CanonicalName), object> TouchedSources { get; } = new Dictionary<(Type SourceType, string CanonicalName), object>();
}
