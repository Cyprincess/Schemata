using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Entities;

namespace Schemata.Flow.Foundation;

/// <summary>Joined repositories, resource mutations, and unit of work for a Flow operation.</summary>
/// <typeparam name="TProcess">The persisted process entity type registered by the host.</typeparam>
/// <typeparam name="TToken">The persisted token entity type registered by the host.</typeparam>
/// <typeparam name="TTransition">The persisted transition entity type registered by the host.</typeparam>
/// <typeparam name="TCompensation">The persisted compensation entity type registered by the host.</typeparam>
/// <typeparam name="TSource">The persisted source-binding entity type registered by the host.</typeparam>
public sealed class FlowPersistenceScope<TProcess, TToken, TTransition, TCompensation, TSource>(
    IUnitOfWork          unitOfWork,
    IRepository<TProcess> processes,
    IRepository<TToken>   tokens,
    IRepository<TTransition> transitions,
    IRepository<TSource>     sources,
    IRepository<TCompensation> compensations,
    IServiceProvider           services
)
    where TProcess : SchemataProcess
    where TToken : SchemataProcessToken
    where TTransition : SchemataProcessTransition
    where TCompensation : SchemataProcessCompensation
    where TSource : SchemataProcessSource
{
    /// <summary>The unit of work shared by all repositories and mutations.</summary>
    public IUnitOfWork UnitOfWork { get; } = unitOfWork;

    public IRepository<TProcess> Processes { get; } = processes;

    public IRepository<TToken> Tokens { get; } = tokens;

    public IRepository<TTransition> Transitions { get; } = transitions;

    public IRepository<TSource> Sources { get; } = sources;

    public IRepository<TCompensation> Compensations { get; } = compensations;

    internal HashSet<TProcess> CreatedProcesses { get; } = new(ReferenceEqualityComparer.Instance);

    internal HashSet<TToken> CreatedTokens { get; } = new(ReferenceEqualityComparer.Instance);

    private readonly Dictionary<string, Dictionary<string, TToken>> _loadedTokens = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<TCompensation>> _compensations = new(StringComparer.Ordinal);
    private Dictionary<(string Process, string? Token, string Name), TSource>? _sourceBindings;

    internal TSource? FindSourceBinding(string process, string? token, string name) {
        return _sourceBindings is { } bindings && bindings.TryGetValue((process, token, name), out var binding) ? binding : null;
    }

    internal void TrackSourceBinding(TSource binding) {
        (_sourceBindings ??= new())[(binding.Process, binding.Token, binding.Name)] = binding;
    }

    internal IEnumerable<TSource> StagedSourceBindings => _sourceBindings is { } bindings
        ? bindings.Values
        : Array.Empty<TSource>();


    /// <summary>Resolves the resource mutation owner that stages through <see cref="UnitOfWork" />.</summary>
    internal IResourceMutation<TEntity> Mutation<TEntity>()
        where TEntity : class {
        return services.GetRequiredService<IResourceMutation<TEntity>>();
    }

    internal async Task<List<TCompensation>> GetCompensationsAsync(string process, CancellationToken ct) {
        if (_compensations.TryGetValue(process, out var rows)) return rows;
        rows = await Compensations.ListAsync<TCompensation>(q => q.Where(row => row.Process == process), ct, new QueryOptions(IgnoreOwner: true)).ToListAsync(ct);
        _compensations.Add(process, rows);
        return rows;
    }

    internal void IndexTokens(string processName, IReadOnlyList<TToken> tokens) {
        var index = new Dictionary<string, TToken>(tokens.Count, StringComparer.Ordinal);
        foreach (var token in tokens) {
            if (token.CanonicalName is { } name) index.Add(name, token);
        }
        _loadedTokens[processName] = index;
    }

    internal async Task<Dictionary<string, TToken>> GetTokenIndexAsync(string processName, CancellationToken ct) {
        if (_loadedTokens.TryGetValue(processName, out var index)) return index;
        index = new(StringComparer.Ordinal);
        await foreach (var token in Tokens.ListAsync<TToken>(q => q.Where(t => t.Process == processName), ct, new QueryOptions(IgnoreOwner: true))) {
            if (token.CanonicalName is { } name) index.Add(name, token);
        }
        foreach (var token in CreatedTokens) {
            if (token.Process == processName && token.CanonicalName is { } name) index[name] = token;
        }
        _loadedTokens.Add(processName, index);
        return index;
    }

    internal async Task CreateProcessAsync(TProcess process, CancellationToken ct) {
        await Mutation<TProcess>().CreateAsync(process, UnitOfWork, ct);
        CreatedProcesses.Add(process);
    }

    internal async Task CreateTokenAsync(TToken token, CancellationToken ct) {
        await Mutation<TToken>().CreateAsync(token, UnitOfWork, ct);
        CreatedTokens.Add(token);
        if (token.CanonicalName is { } name && _loadedTokens.TryGetValue(token.Process, out var index)) index[name] = token;
    }
}
