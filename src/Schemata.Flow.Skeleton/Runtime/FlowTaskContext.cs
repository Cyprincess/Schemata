using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>
///     Runtime context passed to a procedure task body for the current token.
/// </summary>
public sealed class FlowTaskContext
{
    private readonly FlowExecutionContext _execution;

    /// <summary>Initializes a flow task context.</summary>
    /// <param name="definition">The process definition being executed.</param>
    /// <param name="process">The persisted process instance being advanced.</param>
    /// <param name="token">The token addressed by the task.</param>
    /// <param name="execution">The shared execution context for the current engine call.</param>
    /// <param name="payload">The event payload delivered to typed procedure tasks.</param>
    public FlowTaskContext(
        ProcessDefinition     definition,
        SchemataProcess       process,
        SchemataProcessToken  token,
        FlowExecutionContext  execution,
        object?               payload = null
    ) {
        Definition = definition;
        Process    = process;
        Token      = token;
        _execution = execution;
        Payload    = payload;
    }

    /// <summary>The process definition being executed.</summary>
    public ProcessDefinition Definition { get; }

    /// <summary>The persisted process instance being advanced.</summary>
    public SchemataProcess Process { get; }

    /// <summary>The token addressed by the task.</summary>
    public SchemataProcessToken Token { get; }

    /// <summary>The unit of work shared by the current engine call.</summary>
    public IUnitOfWork UnitOfWork => _execution.UnitOfWork;

    internal bool TrackSources { get; set; } = true;

    /// <summary>The event payload delivered to typed procedure tasks and conditions.</summary>
    public object? Payload { get; }

    /// <summary>Loads the source binding named after <typeparamref name="TEntity" />.</summary>
    /// <typeparam name="TEntity">The source entity type.</typeparam>
    /// <param name="ct">A cancellation token.</param>
    public ValueTask<TEntity> SourceAsync<TEntity>(CancellationToken ct = default)
        where TEntity : class, ICanonicalName {
        return SourceAsync<TEntity>(DefaultSourceName<TEntity>(), ct);
    }

    /// <summary>Loads the source binding with the supplied name.</summary>
    /// <typeparam name="TEntity">The source entity type.</typeparam>
    /// <param name="name">The binding name.</param>
    /// <param name="ct">A cancellation token.</param>
    public async ValueTask<TEntity> SourceAsync<TEntity>(string name, CancellationToken ct = default)
        where TEntity : class, ICanonicalName {
        var binding = await FindSourceAsync(name, ct);
        if (binding is null) {
            throw new InvalidOperationException($"Source binding '{name}' was not found for process '{Process.CanonicalName}'.");
        }

        var repository = Repository<TEntity>();
        var source = await repository.SingleOrDefaultAsync(q => q.Where(e => e.CanonicalName == binding.Source), ct);
        if (source is null) {
            throw new InvalidOperationException($"Source entity '{binding.Source}' was not found for binding '{name}'.");
        }

        TrackSource(source);
        return source;
    }

    /// <summary>Resolves a repository enlisted in the current unit of work.</summary>
    /// <typeparam name="TEntity">The entity type managed by the repository.</typeparam>
    public IRepository<TEntity> Repository<TEntity>()
        where TEntity : class {
        return GetRequiredService<IRepository<TEntity>>();
    }

    /// <summary>
    ///     Resolves an optional service from the scoped provider. A resolved
    ///     <see cref="IRepository" /> is enlisted in the current unit of work before it is returned.
    /// </summary>
    /// <typeparam name="TService">The service type to resolve.</typeparam>
    /// <param name="key">The service key for keyed registrations; <see langword="null" /> resolves the default registration.</param>
    public TService? GetService<TService>(object? key = null)
        where TService : class {
        var service = key is null
            ? _execution.Services.GetService<TService>()
            : _execution.Services.GetKeyedService<TService>(key);
        if (service is IRepository repository) {
            repository.Join(UnitOfWork);
        }

        return service;
    }

    /// <summary>
    ///     Resolves a required service from the scoped provider. A resolved
    ///     <see cref="IRepository" /> is enlisted in the current unit of work before it is returned.
    /// </summary>
    /// <typeparam name="TService">The service type to resolve.</typeparam>
    /// <param name="key">The service key for keyed registrations; <see langword="null" /> resolves the default registration.</param>
    public TService GetRequiredService<TService>(object? key = null)
        where TService : class {
        var service = key is null
            ? _execution.Services.GetRequiredService<TService>()
            : _execution.Services.GetRequiredKeyedService<TService>(key);
        if (service is IRepository repository) {
            repository.Join(UnitOfWork);
        }

        return service;
    }

    /// <summary>Binds the current token to an entity under the default source name.</summary>
    /// <typeparam name="TEntity">The source entity type.</typeparam>
    /// <param name="entity">The source entity.</param>
    /// <param name="ct">A cancellation token.</param>
    public ValueTask BindSourceAsync<TEntity>(TEntity entity, CancellationToken ct = default)
        where TEntity : class, ICanonicalName {
        return BindSourceAsync(DefaultSourceName<TEntity>(), entity, ct);
    }

    /// <summary>Binds the current token to an entity under the supplied source name.</summary>
    /// <typeparam name="TEntity">The source entity type.</typeparam>
    /// <param name="name">The binding name.</param>
    /// <param name="entity">The source entity.</param>
    /// <param name="ct">A cancellation token.</param>
    public async ValueTask BindSourceAsync<TEntity>(string name, TEntity entity, CancellationToken ct = default)
        where TEntity : class, ICanonicalName {
        if (string.IsNullOrEmpty(entity.CanonicalName)) {
            throw new InvalidOperationException($"Source entity type '{typeof(TEntity).FullName}' has no canonical name.");
        }

        var repository = Repository<SchemataProcessSource>();
        var process    = ProcessCanonicalName();
        var token      = Token.CanonicalName;
        var source     = await repository.SingleOrDefaultAsync(q => q.Where(s => s.Process == process && s.Token == token && s.Name == name), ct);
        if (source is null) {
            source = new() { Process = process, Token = token, Name = name };
            AssignSource(source, entity);
            await repository.AddAsync(source, ct);
        } else {
            AssignSource(source, entity);
            await repository.UpdateAsync(source, ct);
        }

        TrackSource(entity);
    }

    private static string DefaultSourceName<TEntity>() { return typeof(TEntity).Name.Underscore().ToLowerInvariant(); }

    private async ValueTask<SchemataProcessSource?> FindSourceAsync(string name, CancellationToken ct) {
        var repository = Repository<SchemataProcessSource>();
        var process    = ProcessCanonicalName();
        var token      = Token.CanonicalName;

        if (!string.IsNullOrEmpty(token)) {
            var scoped = await repository.SingleOrDefaultAsync(q => q.Where(s => s.Process == process && s.Token == token && s.Name == name), ct);
            if (scoped is not null) {
                return scoped;
            }
        }

        return await repository.SingleOrDefaultAsync(q => q.Where(s => s.Process == process && s.Token == null && s.Name == name), ct);
    }

    private string ProcessCanonicalName() {
        if (!string.IsNullOrEmpty(Process.CanonicalName)) {
            return Process.CanonicalName;
        }

        throw new InvalidOperationException("The process has no canonical name.");
    }

    private static void AssignSource<TEntity>(SchemataProcessSource source, TEntity entity)
        where TEntity : class, ICanonicalName {
        source.SourceType      = typeof(TEntity).FullName ?? typeof(TEntity).Name;
        source.Source          = entity.CanonicalName!;
        source.SourceTimestamp = entity is IConcurrency concurrent ? concurrent.Timestamp : null;
    }

    private void TrackSource<TEntity>(TEntity entity)
        where TEntity : class, ICanonicalName {
        if (TrackSources && !string.IsNullOrEmpty(entity.CanonicalName)) {
            _execution.TouchedSources[(typeof(TEntity), entity.CanonicalName)] = entity;
        }
    }
}
