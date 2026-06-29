using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Foundation;

/// <summary>
///     Builds the unit-of-work callback that projects a process transition back onto its source
///     business entity. The state-machine engine is the single-token specialization of BPMN, so the
///     default projection writes <see cref="ProcessInstance.State" /> to an
///     <see cref="IStateful" /> source; a multi-token engine overrides this through a keyed
///     <see cref="IFlowWritebackProjector" />.
/// </summary>
internal static class ProcessWriteback
{
    private static readonly ConcurrentDictionary<Type, IWritebackWorker?> Workers = new();

    public static Func<IUnitOfWork, CancellationToken, Task>? Build(
        IServiceProvider services,
        SchemataProcess  process,
        ProcessInstance  instance,
        string?          engine
    ) {
        if (string.IsNullOrEmpty(process.Source) || string.IsNullOrEmpty(process.SourceType)) {
            return null;
        }

        if (services.GetService<IOptions<SchemataFlowOptions>>()?.Value is { SourceWriteback: false }) {
            return null;
        }

        var type = AppDomainTypeCache.GetType(process.SourceType);
        if (type is null) {
            return null;
        }

        var worker = Workers.GetOrAdd(type, CreateWorker);
        if (worker is null) {
            return null;
        }

        return (uow, ct) => worker.WriteBackAsync(services, uow, process, instance, engine, ct);
    }

    private static IWritebackWorker? CreateWorker(Type type) {
        if (!typeof(ICanonicalName).IsAssignableFrom(type)) {
            return null;
        }

        return (IWritebackWorker?)Activator.CreateInstance(typeof(WritebackWorker<>).MakeGenericType(type));
    }

    #region Nested type: IWritebackWorker

    private interface IWritebackWorker
    {
        Task WriteBackAsync(
            IServiceProvider  services,
            IUnitOfWork       uow,
            SchemataProcess   process,
            ProcessInstance   instance,
            string?           engine,
            CancellationToken ct
        );
    }

    #endregion

    #region Nested type: WritebackWorker

    private sealed class WritebackWorker<TSource> : IWritebackWorker
        where TSource : class, ICanonicalName
    {
        #region IWritebackWorker Members

        public async Task WriteBackAsync(
            IServiceProvider  services,
            IUnitOfWork       uow,
            SchemataProcess   process,
            ProcessInstance   instance,
            string?           engine,
            CancellationToken ct
        ) {
            var repository = services.GetService<IRepository<TSource>>();
            if (repository is null) {
                return;
            }

            repository.Join(uow);

            var name   = process.Source;
            var entity = await repository.FirstOrDefaultAsync(q => q.Where(e => e.CanonicalName == name), ct);
            if (entity is null) {
                return;
            }

            if (process.SourceTimestamp is { } expected && entity is IConcurrency concurrency
             && concurrency.Timestamp != expected) {
                throw new FailedPreconditionException(
                    SchemataResources.FLOW_SOURCE_MODIFIED_CONCURRENTLY,
                    new Dictionary<string, string> { ["name"] = name ?? string.Empty });
            }

            var projector = engine is null
                ? null
                : services.GetKeyedService<IFlowWritebackProjector>(engine);

            if (projector is not null) {
                projector.Project(entity, process, instance);
            } else if (entity is IStateful stateful) {
                stateful.State = instance.State;
            }

            await repository.UpdateAsync(entity, ct);

            if (entity is IConcurrency stamped) {
                process.SourceTimestamp = stamped.Timestamp;
            }
        }

        #endregion
    }

    #endregion
}
