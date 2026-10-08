using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Tenancy;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Entities;

namespace Schemata.Flow.Foundation;

/// <summary>Joined repositories, resource mutations, and unit of work for a Flow operation.</summary>
public sealed class FlowPersistenceScope(
    IUnitOfWork                              unitOfWork,
    IRepository<SchemataProcess>             processes,
    IRepository<SchemataProcessToken>        tokens,
    IRepository<SchemataProcessTransition>   transitions,
    IRepository<SchemataProcessSource>       sources,
    IRepository<SchemataProcessCompensation> compensations,
    IServiceProvider                         services
)
{
    /// <summary>The unit of work shared by all repositories and mutations.</summary>
    public IUnitOfWork UnitOfWork { get; } = unitOfWork;

    public IRepository<SchemataProcess> Processes { get; } = processes;

    public IRepository<SchemataProcessToken> Tokens { get; } = tokens;

    public IRepository<SchemataProcessTransition> Transitions { get; } = transitions;

    public IRepository<SchemataProcessSource> Sources { get; } = sources;

    public IRepository<SchemataProcessCompensation> Compensations { get; } = compensations;

    internal HashSet<SchemataProcess> CreatedProcesses { get; } = new(ReferenceEqualityComparer.Instance);

    internal HashSet<SchemataProcessToken> CreatedTokens { get; } = new(ReferenceEqualityComparer.Instance);

    private readonly Dictionary<string, Dictionary<string, SchemataProcessToken>> _loadedTokens = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<SchemataProcessCompensation>> _compensations = new(StringComparer.Ordinal);
    private Dictionary<(string Process, string? Token, string Name), SchemataProcessSource>? _sourceBindings;

    internal SchemataProcessSource? FindSourceBinding(string process, string? token, string name) {
        return _sourceBindings is { } bindings && bindings.TryGetValue((process, token, name), out var binding) ? binding : null;
    }

    internal void TrackSourceBinding(SchemataProcessSource binding) {
        (_sourceBindings ??= new())[(binding.Process, binding.Token, binding.Name)] = binding;
    }

    internal IEnumerable<SchemataProcessSource> StagedSourceBindings => _sourceBindings is { } bindings
        ? bindings.Values
        : Array.Empty<SchemataProcessSource>();


    /// <summary>Resolves the resource mutation owner that stages through <see cref="UnitOfWork" />.</summary>
    internal IResourceMutation<TEntity> Mutation<TEntity>()
        where TEntity : class {
        return services.GetRequiredService<IResourceMutation<TEntity>>();
    }

    internal async Task<List<SchemataProcessCompensation>> GetCompensationsAsync(string process, CancellationToken ct) {
        if (_compensations.TryGetValue(process, out var rows)) return rows;
        rows = await Compensations.ListAsync<SchemataProcessCompensation>(q => q.Where(row => row.Process == process), ct).ToListAsync(ct);
        _compensations.Add(process, rows);
        return rows;
    }

    internal void IndexTokens(string processName, IReadOnlyList<SchemataProcessToken> tokens) {
        var index = new Dictionary<string, SchemataProcessToken>(tokens.Count, StringComparer.Ordinal);
        foreach (var token in tokens) {
            if (token.CanonicalName is { } name) index.Add(name, token);
        }
        _loadedTokens[processName] = index;
    }

    internal async Task<Dictionary<string, SchemataProcessToken>> GetTokenIndexAsync(string processName, CancellationToken ct) {
        if (_loadedTokens.TryGetValue(processName, out var index)) return index;
        index = new(StringComparer.Ordinal);
        await foreach (var token in Tokens.ListAsync<SchemataProcessToken>(q => q.Where(t => t.Process == processName), ct)) {
            if (token.CanonicalName is { } name) index.Add(name, token);
        }
        foreach (var token in CreatedTokens) {
            if (token.Process == processName && token.CanonicalName is { } name) index[name] = token;
        }
        _loadedTokens.Add(processName, index);
        return index;
    }

    internal async Task CreateProcessAsync(SchemataProcess process, CancellationToken ct) {
        process.TenantUid = TenantContext.Current.Uid;
        await Mutation<SchemataProcess>().CreateAsync(process, UnitOfWork, ct);
        CreatedProcesses.Add(process);
    }

    internal async Task CreateTokenAsync(SchemataProcessToken token, CancellationToken ct) {
        token.TenantUid = TenantContext.Current.Uid;
        await Mutation<SchemataProcessToken>().CreateAsync(token, UnitOfWork, ct);
        CreatedTokens.Add(token);
        if (token.CanonicalName is { } name && _loadedTokens.TryGetValue(token.Process, out var index)) index[name] = token;
    }
}
