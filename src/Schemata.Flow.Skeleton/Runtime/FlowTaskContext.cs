using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
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
        return SourceAsync<TEntity>(FlowSourceDescriptor.DefaultBindingName<TEntity>(), ct);
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

        if (_execution.TouchedSources.TryGetValue((typeof(TEntity), binding.Source), out var touched)) {
            if (TrackSources && touched is IConcurrency trackedStamp) {
                await _execution.TrackSourceStampAsync(ProcessCanonicalName(), typeof(TEntity), binding.Source, trackedStamp.Timestamp, ct);
            }
            return (TEntity)touched;
        }

        var repository = Repository<TEntity>();
        TEntity? source;
        using (_execution.SourceReadGuard?.Invoke(repository)) {
            source = await repository.SingleOrDefaultAsync(q => q.Where(e => e.CanonicalName == binding.Source), ct);
        }

        if (source is null) {
            throw new InvalidOperationException($"Source entity '{binding.Source}' was not found for binding '{name}'.");
        }

        if (TrackSources && source is IConcurrency concurrency) {
            await _execution.TrackSourceStampAsync(ProcessCanonicalName(), typeof(TEntity), binding.Source, concurrency.Timestamp, ct);
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
    ///     Resolves every registration of a service from the scoped provider. A resolved
    ///     <see cref="IRepository" /> is enlisted in the current unit of work before it is returned.
    /// </summary>
    /// <typeparam name="TService">The service type to resolve.</typeparam>
    /// <param name="key">The service key for keyed registrations; <see langword="null" /> resolves the default registrations.</param>
    public IEnumerable<TService> GetServices<TService>(object? key = null)
        where TService : class {
        var services = key is null
            ? _execution.Services.GetServices<TService>()
            : _execution.Services.GetKeyedServices<TService>(key);
        foreach (var service in services) {
            if (service is IRepository repository) {
                repository.Join(UnitOfWork);
            }
        }

        return services;
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
        return BindSourceAsync(FlowSourceDescriptor.DefaultBindingName<TEntity>(), entity, ct);
    }

    /// <summary>Binds the current token to an entity under the supplied source name.</summary>
    /// <typeparam name="TEntity">The source entity type.</typeparam>
    /// <param name="name">The binding name.</param>
    /// <param name="entity">The source entity.</param>
    /// <param name="ct">A cancellation token.</param>
    public async ValueTask BindSourceAsync<TEntity>(string name, TEntity entity, CancellationToken ct = default)
        where TEntity : class, ICanonicalName {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(entity.CanonicalName)) {
            throw new InvalidOperationException($"Source entity type '{typeof(TEntity).FullName}' has no canonical name.");
        }

        if (_execution.TrackSourceBinding is not { } trackBinding || _execution.FindSourceBinding is not { } findBinding) {
            throw new FailedPreconditionException(
                [new PreconditionViolation { Type = "binding", Subject = Token.StateName, Description = "SourceBindingOwner" }],
                SchemataResources.FLOW_TASK_BINDING_REQUIRED,
                new Dictionary<string, string?> { ["name"] = Token.StateName, ["binding"] = "SourceBindingOwner" });
        }
        if (entity is IConcurrency concurrency) {
            await _execution.TrackSourceStampAsync(ProcessCanonicalName(), typeof(TEntity), entity.CanonicalName, concurrency.Timestamp, ct);
        }

        var repository = Repository<SchemataProcessSource>();
        var mutation   = _execution.Services.GetRequiredService<IResourceMutation<SchemataProcessSource>>();
        var process    = ProcessCanonicalName();
        var token      = Token.CanonicalName;
        var source = findBinding(process, token, name)
            ?? await repository.SingleOrDefaultAsync(q => q.Where(s => s.Process == process && s.Token == token && s.Name == name), ct);
        if (source is null) {
            source = new() { Process = process, Token = token, Name = name };
            AssignSource(source, entity);
            if (await mutation.CreateAsync(source, UnitOfWork, ct) != MutationResult.Applied) {
                return;
            }
        } else {
            var previous = (source.SourceType, source.Source, source.SourceTimestamp);
            AssignSource(source, entity);
            try {
                if (await mutation.UpdateAsync(source, UnitOfWork, ct: ct) != MutationResult.Applied) {
                    (source.SourceType, source.Source, source.SourceTimestamp) = previous;
                    return;
                }
            } catch {
                (source.SourceType, source.Source, source.SourceTimestamp) = previous;
                throw;
            }
        }

        trackBinding(source);

        TrackSource(entity);
    }

    private async ValueTask<SchemataProcessSource?> FindSourceAsync(string name, CancellationToken ct) {
        var repository = Repository<SchemataProcessSource>();
        var process    = ProcessCanonicalName();
        var token      = Token.CanonicalName;

        if (!string.IsNullOrEmpty(token)) {
            if (_execution.FindSourceBinding?.Invoke(process, token, name) is { } staged) {
                return staged;
            }

            var scoped = await repository.SingleOrDefaultAsync(q => q.Where(s => s.Process == process && s.Token == token && s.Name == name), ct);
            if (scoped is not null) {
                return scoped;
            }
        }

        var spawner = Token.Spawner;
        while (!string.IsNullOrEmpty(spawner)) {
            var parent = _execution.FindTokenAsync is { } findToken
                ? await findToken(Process.Name!, spawner, ct)
                : await Repository<SchemataProcessToken>().SingleOrDefaultAsync(
                    q => q.Where(t => t.Process == Process.Name && t.CanonicalName == spawner), ct);
            if (parent is null || parent.Process != Process.Name) {
                break;
            }

            if (_execution.FindSourceBinding?.Invoke(process, spawner, name) is { } staged) {
                return staged;
            }

            var scoped = await repository.SingleOrDefaultAsync(
                q => q.Where(s => s.Process == process && s.Token == spawner && s.Name == name), ct);
            if (scoped is not null) {
                return scoped;
            }

            spawner = parent.Spawner;
        }

        if (_execution.FindSourceBinding?.Invoke(process, null, name) is { } processBinding) {
            return processBinding;
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
