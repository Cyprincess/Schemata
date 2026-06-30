using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Common.Errors;
using Schemata.Entity.Repository;
using Schemata.Flow.Foundation;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Resource.Foundation;

namespace Schemata.Flow.Http.Internal;

/// <summary>HTTP resource-method handler for starting process instances with transport-loaded sources.</summary>
public sealed class FlowHttpStartProcessHandler(FlowRunner runner, FlowHttpSourceLoader sources)
    : IResourceMethodHandler<SchemataProcess, StartProcessInstanceRequest, SchemataProcess>
{
    public ValueTask<SchemataProcess> InvokeAsync(
        string?                     name,
        StartProcessInstanceRequest request,
        SchemataProcess?            entity,
        ClaimsPrincipal?            principal,
        CancellationToken           ct
    ) {
        var options = new StartProcessOptions {
            DisplayName = request.DisplayName,
            Description = request.Description,
        };

        if (string.IsNullOrWhiteSpace(request.Source)) {
            return runner.StartAsync(request.DefinitionName, options, principal, ct);
        }

        return sources.StartAsync(runner, request.DefinitionName, request.Source!, options, principal, ct);
    }
}

/// <summary>Loads source entities for HTTP start requests before invoking Flow.</summary>
public sealed class FlowHttpSourceLoader(
    IResourceTypeResolver resolver,
    IProcessRegistry      registry,
    IServiceProvider      services
)
{
    private readonly ConcurrentDictionary<Type, ISourceLoadStrategy> _strategies = new();

    public ValueTask<SchemataProcess> StartAsync(
        FlowRunner           runner,
        string               definitionName,
        string               source,
        StartProcessOptions  options,
        ClaimsPrincipal?     principal,
        CancellationToken    ct
    ) {
        var reg = registry.GetRegistration(definitionName);
        if (reg is null) {
            throw new NotFoundException(
                SchemataResources.PROCESS_NOT_REGISTERED,
                new Dictionary<string, string?> { ["name"] = definitionName }
            );
        }

        var type = resolver.Resolve(source);
        if (type is null) {
            throw new NotFoundException(
                SchemataResources.INVALID_REFERENCE,
                new Dictionary<string, string?> { ["value"] = source }
            );
        }

        if (!reg.SourceTypes.Values.Any(descriptor => descriptor.SourceType == type)) {
            throw SchemataResourceErrors.NotFound(type, source);
        }

        var strategy = _strategies.GetOrAdd(type, CreateStrategy);
        return strategy.StartAsync(services, runner, definitionName, source, options, principal, ct);
    }

    private static ISourceLoadStrategy CreateStrategy(Type type) {
        var strategy = Activator.CreateInstance(typeof(SourceLoadStrategy<>).MakeGenericType(type));
        if (strategy is ISourceLoadStrategy typed) {
            return typed;
        }

        throw new InvalidOperationException($"Source load strategy for '{type.FullName}' could not be created.");
    }

    private interface ISourceLoadStrategy
    {
        ValueTask<SchemataProcess> StartAsync(
            IServiceProvider     services,
            FlowRunner           runner,
            string               definitionName,
            string               source,
            StartProcessOptions  options,
            ClaimsPrincipal?     principal,
            CancellationToken    ct
        );
    }

    private sealed class SourceLoadStrategy<TSource> : ISourceLoadStrategy
        where TSource : class, ICanonicalName
    {
        public async ValueTask<SchemataProcess> StartAsync(
            IServiceProvider     services,
            FlowRunner           runner,
            string               definitionName,
            string               source,
            StartProcessOptions  options,
            ClaimsPrincipal?     principal,
            CancellationToken    ct
        ) {
            var entity = await LoadAsync(services, source, ct);
            return await runner.StartAsync(definitionName, entity, options, principal, ct);
        }

        private static async ValueTask<TSource> LoadAsync(IServiceProvider services, string source, CancellationToken ct) {
            var repository = (IRepository<TSource>)services.GetService(typeof(IRepository<TSource>))!;
            var container  = new ResourceRequestContainer<TSource>();
            Apply(container, source);
            TSource? entity;
            using (repository.SuppressQuerySoftDelete()) {
                entity = await repository.SingleOrDefaultAsync(q => container.Query(q), ct);
            }

            if (entity is not null) {
                return entity;
            }

            throw SchemataResourceErrors.NotFound<TSource>(source);
        }

        private static void Apply(ResourceRequestContainer<TSource> container, string source) {
            var descriptor = ResourceNameDescriptor.ForType<TSource>();
            var parsed     = descriptor.ParseCanonicalName(source);
            if (parsed is null) {
                throw SchemataResourceErrors.NotFound<TSource>(source);
            }

            var (parents, leaf) = parsed.Value;
            container.ApplyModification(r => r.Name == leaf);
            container.ApplyModification(descriptor.BuildParentPredicate<TSource>(parents));
        }
    }
}
