using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Tests;

internal static class FlowTestCreation
{
    private static long _next;

    internal static void Register(IServiceCollection services) {
        services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(IRepositoryAddAdvisor<>), typeof(NameAdvisor<>)));
    }

    internal static void Assign(object entity) {
        if (entity is not ICanonicalName named) {
            return;
        }

        named.Name ??= $"consumer-{Interlocked.Increment(ref _next)}";
        var descriptor = ResourceNameDescriptor.ForType(entity.GetType());
        if (descriptor.Pattern is not null) {
            named.CanonicalName = descriptor.Resolve(entity);
        }
        if (entity is IIdentifier identifier && identifier.Uid == Guid.Empty) {
            identifier.Uid = Guid.NewGuid();
        }
    }

    internal static FlowExecutionContext Context(
        IUnitOfWork unitOfWork,
        IServiceProvider services,
        IReadOnlyList<ProcessCompensationBinding>? bindings = null,
        Func<ProcessSnapshot, CancellationToken, Task>? persist = null
    ) {
        return new(unitOfWork, services) {
            CreateProcessAsync = (entity, _) => { Assign(entity); return Task.CompletedTask; },
            CreateTokenAsync = (entity, _) => { Assign(entity); return Task.CompletedTask; },
            PersistSnapshotAsync = persist ?? ((_, _) => throw new InvalidOperationException("This engine fixture has no snapshot storage.")),
            LoadedCompensationBindings = bindings ?? [],
        };
    }

    internal sealed class NameAdvisor<TEntity> : IRepositoryAddAdvisor<TEntity> where TEntity : class
    {
        public int Order => AdviceAddCanonicalName.DefaultOrder - 1;

        public Task<AdviseResult> AdviseAsync(AdviceContext context, IRepository<TEntity> repository, TEntity entity, CancellationToken ct) {
            if (entity is ICanonicalName named) {
                named.Name ??= $"consumer-{Interlocked.Increment(ref _next)}";
            }
            return Task.FromResult(AdviseResult.Continue);
        }
    }
}
