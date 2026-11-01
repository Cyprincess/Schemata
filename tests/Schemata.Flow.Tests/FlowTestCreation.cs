using System;
using System.Collections.Generic;
using System.Linq;
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
        var sources  = services.GetService<IRepository<SchemataProcessSource>>();
        var mutation = services.GetService<IResourceMutation<SchemataProcessSource>>();

        return new(unitOfWork, services) {
            CreateProcessAsync = (entity, _) => { Assign(entity); return Task.CompletedTask; },
            CreateTokenAsync = (entity, _) => { Assign(entity); return Task.CompletedTask; },
            PersistSnapshotAsync = persist ?? ((_, _) => throw new InvalidOperationException("This engine fixture has no snapshot storage.")),
            NewSourceBinding = (process, token, name) => new() { Process = process, Token = token, Name = name },
            FindPersistedSourceAsync = sources is null
                ? null
                : async (process, token, name, ct) => await sources.SingleOrDefaultAsync(
                    query => query.Where(row => row.Process == process && row.Token == token && row.Name == name), ct),
            ListSourceBindingsAsync = sources is null
                ? null
                : (process, canonical, type, ct) => sources.ListAsync<SchemataProcessSource>(
                    query => query.Where(row => row.Process == process && row.Source == canonical && row.SourceType == type), ct),
            CreateSourceBindingAsync = mutation is null
                ? null
                : async (row, ct) => await mutation.CreateAsync(row, unitOfWork, ct),
            UpdateSourceBindingAsync = mutation is null
                ? null
                : async (row, ct) => await mutation.UpdateAsync(row, unitOfWork, ct: ct),
            LoadedCompensationBindings = bindings ?? [],
        };
    }

    internal sealed class NameAdvisor<TEntity> : IRepositoryAddAdvisor<TEntity> where TEntity : class
    {
        public int Order => AdviceAddCanonicalName.DefaultOrder - 1;

        public Task<AdviseResult> AdviseAsync(AdviceContext context, IRepository<TEntity> repository, TEntity entity, MutationOptions options, CancellationToken ct) {
            if (entity is ICanonicalName named) {
                named.Name ??= $"consumer-{Interlocked.Increment(ref _next)}";
            }
            return Task.FromResult(AdviseResult.Continue);
        }
    }
}
