using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Entities;
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

    /// <summary>Stages a process through the joined repository before its name is referenced.</summary>
    public required Func<SchemataProcess, CancellationToken, Task> CreateProcessAsync { get; init; }

    /// <summary>Stages a token through the joined repository before its name is referenced.</summary>
    public required Func<SchemataProcessToken, CancellationToken, Task> CreateTokenAsync { get; init; }

    /// <summary>Persists a called process snapshot within the current unit of work.</summary>
    public required Func<ProcessSnapshot, CancellationToken, Task> PersistSnapshotAsync { get; init; }

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

    /// <summary>Resolves persisted and staged tokens from the Flow persistence owner's index.</summary>
    public Func<string, string, CancellationToken, ValueTask<SchemataProcessToken?>>? FindTokenAsync { get; init; }

    /// <summary>Reads bindings staged by the shared Flow persistence owner before database publication.</summary>
    public Func<string, string?, string, SchemataProcessSource?>? FindSourceBinding { get; init; }

    /// <summary>Retains an applied binding mutation in the shared Flow persistence scope.</summary>
    public Action<SchemataProcessSource>? TrackSourceBinding { get; init; }

    /// <summary>Checks persisted binding expectations before the operation mutates a loaded source.</summary>
    public Func<string, Type, string, Guid, CancellationToken, ValueTask>? ValidateSourceAsync { get; init; }

    /// <summary>Compensation handlers restored from persisted process state for this engine operation.</summary>
    public IReadOnlyList<ProcessCompensationBinding> LoadedCompensationBindings { get; init; } = [];

    internal List<ProcessCompensationBinding> CompensationBindings { get; } = [];

    internal bool CompensationBindingsLoaded { get; set; }

    internal IDictionary<(Type SourceType, string CanonicalName), object> TouchedSources { get; init; } = new Dictionary<(Type SourceType, string CanonicalName), object>();

    internal IDictionary<(Type SourceType, string CanonicalName), Guid> InitialSourceStamps { get; init; } = new Dictionary<(Type SourceType, string CanonicalName), Guid>();

    internal async ValueTask TrackSourceStampAsync(string process, Type type, string canonical, Guid current, CancellationToken ct) {
        var key = (type, canonical);
        var initial = InitialSourceStamps.TryGetValue(key, out var stamp) ? stamp : current;
        if (ValidateSourceAsync is { } validate) {
            await validate(process, type, canonical, initial, ct);
        }
        InitialSourceStamps.TryAdd(key, initial);
    }
}
