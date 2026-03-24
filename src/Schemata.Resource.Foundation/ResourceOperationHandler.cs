using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Parlot;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Advice;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Mapping.Skeleton;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Foundation.Grammars;
using Schemata.Resource.Foundation.Models;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Foundation;

/// <summary>
/// Orchestrates CRUD operations for a resource, running the advisor pipeline around each step.
/// </summary>
/// <typeparam name="TEntity">The persistent entity type.</typeparam>
/// <typeparam name="TRequest">The request DTO type for create and update operations.</typeparam>
/// <typeparam name="TDetail">The detail DTO type returned from get, create, and update operations.</typeparam>
/// <typeparam name="TSummary">The summary DTO type returned from list operations.</typeparam>
/// <remarks>
/// <para>The handler follows a consistent advisor pipeline for each operation:</para>
/// <list type="number">
///   <item><see cref="Advisors.IResourceRequestAdvisor{TEntity}"/> -- general request-level check</item>
///   <item>Operation-specific request advisor (e.g. <see cref="Advisors.IResourceCreateRequestAdvisor{TEntity, TRequest}"/>)</item>
///   <item>Operation-specific entity advisor (e.g. <see cref="Advisors.IResourceCreateAdvisor{TEntity, TRequest}"/>)</item>
///   <item>Persistence (add/update/remove + commit)</item>
///   <item><see cref="Advisors.IResourceResponseAdvisor{TEntity, TDetail}"/> -- response post-processing</item>
/// </list>
/// <para>Registered as a scoped service by <see cref="Features.SchemataResourceFeature"/>.</para>
/// </remarks>
public sealed class ResourceOperationHandler<TEntity, TRequest, TDetail, TSummary>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
    where TSummary : class, ICanonicalName
{
    private readonly ISimpleMapper        _mapper;
    private readonly IRepository<TEntity> _repository;
    private readonly IServiceProvider     _sp;

    /// <summary>
    /// Initializes a new instance of the handler with its required dependencies.
    /// </summary>
    /// <param name="sp">The service provider for resolving advisors and options.</param>
    /// <param name="repository">The entity repository.</param>
    /// <param name="mapper">The mapper for converting between entity and DTO types.</param>
    public ResourceOperationHandler(IServiceProvider sp, IRepository<TEntity> repository, ISimpleMapper mapper) {
        _sp         = sp;
        _repository = repository;
        _mapper     = mapper;
    }

    /// <summary>
    /// Finds an entity by its resource name, including soft-deleted entities.
    /// </summary>
    /// <param name="name">The resource name to look up.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The entity if found; otherwise <see langword="null"/>.</returns>
    public async Task<TEntity?> FindByNameAsync(string? name, CancellationToken? ct) {
        return await FindByNameAsync(name, null, ct);
    }

    /// <summary>
    /// Finds an entity by its resource name and optional parent values, including soft-deleted entities.
    /// </summary>
    /// <param name="name">The resource name to look up.</param>
    /// <param name="parentValues">Optional parent resource identifiers for hierarchical resources.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The entity if found; otherwise <see langword="null"/>.</returns>
    public async Task<TEntity?> FindByNameAsync(
        string?                     name,
        Dictionary<string, string>? parentValues,
        CancellationToken?          ct
    ) {
        if (string.IsNullOrWhiteSpace(name)) {
            throw new InvalidArgumentException(message: SchemataResources.GetResourceString(SchemataResources.ST1010)) {
                Details = [new BadRequestDetail {
                    FieldViolations = [new() {
                        Field       = "name",
                        Description = SchemataResources.GetResourceString(SchemataResources.ST1010),
                        Reason      = FieldReasons.Required,
                    }],
                }],
            };
        }

        ct ??= CancellationToken.None;

        var repository = _repository.Once().SuppressQuerySoftDelete();

        if (parentValues is null or { Count: 0 }) {
            return await repository.SingleOrDefaultAsync(q => q.Where(BuildNamePredicate(name)), ct.Value);
        }

        var descriptor      = ResourceNameDescriptor.ForType<TEntity>();
        var parentPredicate = descriptor.BuildParentPredicate<TEntity>(parentValues);
        var namePredicate   = BuildNamePredicate(name);

        if (parentPredicate is null) {
            return await repository.SingleOrDefaultAsync(q => q.Where(namePredicate), ct.Value);
        }

        return await repository.SingleOrDefaultAsync(q => q.Where(namePredicate).Where(parentPredicate), ct.Value);
    }

    /// <summary>
    /// Finds an entity by its canonical name, parsing parent segments automatically.
    /// </summary>
    /// <param name="name">The full canonical resource name (e.g. <c>publishers/123/books/456</c>).</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The entity if found; otherwise <see langword="null"/>.</returns>
    public async Task<TEntity?> FindByCanonicalNameAsync(string? name, CancellationToken? ct) {
        if (string.IsNullOrWhiteSpace(name)) {
            throw new InvalidArgumentException(message: SchemataResources.GetResourceString(SchemataResources.ST1010)) {
                Details = [new BadRequestDetail {
                    FieldViolations = [new() {
                        Field       = "name",
                        Description = SchemataResources.GetResourceString(SchemataResources.ST1010),
                        Reason      = FieldReasons.Required,
                    }],
                }],
            };
        }

        ct ??= CancellationToken.None;

        var descriptor = ResourceNameDescriptor.ForType<TEntity>();
        var parsed     = descriptor.ParseCanonicalName(name);

        if (parsed is null) {
            return null;
        }

        var (parentValues, leafName) = parsed.Value;
        return await FindByNameAsync(leafName, parentValues, ct);
    }

    /// <summary>
    /// Gets an entity by name or throws <see cref="Schemata.Abstractions.Exceptions.NotFoundException"/> if not found.
    /// </summary>
    /// <param name="name">The resource name.</param>
    /// <param name="parentValues">Optional parent resource identifiers for hierarchical resources.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The entity.</returns>
    public async Task<TEntity> GetByNameAsync(
        string?                     name,
        Dictionary<string, string>? parentValues,
        CancellationToken?          ct
    ) {
        return await FindByNameAsync(name, parentValues, ct) ?? throw ResourceNotFound(name);
    }

    /// <summary>
    /// Gets an entity by name, extracting parent values from the HTTP route, or throws if not found.
    /// </summary>
    /// <param name="name">The resource name.</param>
    /// <param name="http">The HTTP context for extracting parent route values.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The entity.</returns>
    public Task<TEntity> GetByNameAsync(string? name, HttpContext? http, CancellationToken? ct) {
        var parentValues = http is not null
            ? ResourceNameDescriptor.ForType<TEntity>().ExtractParentValues(http.Request.RouteValues)
            : null;
        return GetByNameAsync(name, parentValues, ct);
    }

    /// <summary>
    /// Gets an entity by canonical name or throws <see cref="Schemata.Abstractions.Exceptions.NotFoundException"/> if not found.
    /// </summary>
    /// <param name="name">The full canonical resource name.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The entity.</returns>
    public async Task<TEntity> GetByCanonicalNameAsync(string? name, CancellationToken? ct) {
        return await FindByCanonicalNameAsync(name, ct) ?? throw ResourceNotFound(name);
    }

    /// <summary>
    /// Lists entities with filtering, ordering, pagination, and the full advisor pipeline.
    /// </summary>
    /// <param name="request">The list request containing filter, order, paging, and parent parameters.</param>
    /// <param name="http">The optional HTTP context.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A paginated list result with summaries and an optional next page token.</returns>
    public async Task<ListResult<TSummary>> ListAsync(ListRequest request, HttpContext? http, CancellationToken? ct) {
        ct ??= CancellationToken.None;

        var ctx = CreateAdviceContext();

        switch (await Advisor.For<IResourceRequestAdvisor<TEntity>>()
                             .RunAsync(ctx, http, Operations.List, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<ListResult<TSummary>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                return ListResult<TSummary>.Blocked;
        }

        var container = new ResourceRequestContainer<TEntity>();

        switch (await Advisor.For<IResourceListRequestAdvisor<TEntity>>()
                             .RunAsync(ctx, request, container, http, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<ListResult<TSummary>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                return ListResult<TSummary>.Blocked;
        }

        var descriptor = ResourceNameDescriptor.ForType<TEntity>();
        if (!string.IsNullOrWhiteSpace(request.Parent)) {
            var parentValues = descriptor.ParseParent(request.Parent);
            if (parentValues is not null) {
                if (parentValues.Any(kv => kv.Value == "-") && !descriptor.SupportsReadAcross) {
                    throw new InvalidArgumentException(message: SchemataResources.GetResourceString(SchemataResources.ST1013)) {
                        Details = [new BadRequestDetail {
                            FieldViolations = [new() {
                                Field       = "parent",
                                Description = SchemataResources.GetResourceString(SchemataResources.ST1013),
                                Reason      = FieldReasons.CrossParentUnsupported,
                            }],
                        }],
                    };
                }

                var predicate = descriptor.BuildParentPredicate<TEntity>(parentValues);
                if (predicate is not null) {
                    container.ApplyModification(predicate);
                }
            }
        }

        var token = await PageToken.FromStringAsync(request.PageToken)
                 ?? new PageToken {
                        Parent      = request.Parent,
                        Filter      = request.Filter,
                        OrderBy     = request.OrderBy,
                        ShowDeleted = request.ShowDeleted,
                    };
        if (token.Parent != request.Parent
         || token.Filter != request.Filter
         || token.OrderBy != request.OrderBy
         || token.ShowDeleted != request.ShowDeleted) {
            throw new InvalidArgumentException(message: SchemataResources.GetResourceString(SchemataResources.ST1015)) {
                Details = [new BadRequestDetail {
                    FieldViolations = [new() {
                        Field       = nameof(request.PageToken).Underscore(),
                        Description = SchemataResources.GetResourceString(SchemataResources.ST1015),
                        Reason      = FieldReasons.InvalidPageToken,
                    }],
                }],
            };
        }

        if (request.PageSize.HasValue) {
            token.PageSize = request.PageSize.Value;
        }

        token.PageSize = token.PageSize switch {
            <= 0  => 25,
            > 100 => 100,
            var _ => token.PageSize,
        };

        if (request.Skip.HasValue) {
            token.Skip += request.Skip.Value;
        }

        if (token.Skip < 0) {
            token.Skip = 0;
        }

        var repository = _repository.Once();

        if (!string.IsNullOrWhiteSpace(request.Filter)) {
            try {
                var filter = Parser.Filter.Parse(request.Filter);
                container.ApplyFiltering(filter);
            } catch (ParseException) {
                throw new InvalidArgumentException(message: SchemataResources.GetResourceString(SchemataResources.ST1016)) {
                    Details = [new BadRequestDetail {
                        FieldViolations = [new() {
                            Field       = nameof(request.Filter).Underscore(),
                            Description = SchemataResources.GetResourceString(SchemataResources.ST1016),
                            Reason      = FieldReasons.InvalidFilter,
                        }],
                    }],
                };
            }
        }

        if (!string.IsNullOrWhiteSpace(request.OrderBy)) {
            try {
                var order = Parser.Order.Parse(request.OrderBy);
                container.ApplyOrdering(order);
            } catch (ParseException) {
                throw new InvalidArgumentException(message: SchemataResources.GetResourceString(SchemataResources.ST1017)) {
                    Details = [new BadRequestDetail {
                        FieldViolations = [new() {
                            Field       = nameof(request.OrderBy).Underscore(),
                            Description = SchemataResources.GetResourceString(SchemataResources.ST1017),
                            Reason      = FieldReasons.InvalidOrderBy,
                        }],
                    }],
                };
            }
        }

        if (request.ShowDeleted is true) {
            repository = repository.SuppressQuerySoftDelete();
        }

        var totalSize = await repository.CountAsync(q => container.Query(q), ct.Value);

        container.ApplyPaginating(token);

        var entities  = repository.ListAsync(q => container.Query(q), ct.Value);
        var summaries = await _mapper.EachAsync<TEntity, TSummary>(entities, ct.Value).ToListAsync(ct.Value);

        token.Skip += token.PageSize;

        string? nextPageToken = null;
        if (summaries.Count >= token.PageSize) {
            nextPageToken = await token.ToStringAsync();
        }

        var immutable = summaries.ToImmutableArray();

        switch (await Advisor.For<IResourceListResponseAdvisor<TSummary>>()
                             .RunAsync(ctx, immutable, http, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<ListResult<TSummary>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                return ListResult<TSummary>.Blocked;
        }

        return new() {
            TotalSize = totalSize, Entities = immutable, NextPageToken = nextPageToken,
        };
    }

    /// <summary>
    /// Gets a single entity detail through the advisor pipeline.
    /// </summary>
    /// <param name="entity">The entity to retrieve details for.</param>
    /// <param name="http">The optional HTTP context.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The get result containing the detail DTO.</returns>
    public async Task<GetResult<TDetail>> GetAsync(TEntity entity, HttpContext? http, CancellationToken? ct) {
        ct ??= CancellationToken.None;

        var ctx = CreateAdviceContext();

        switch (await Advisor.For<IResourceRequestAdvisor<TEntity>>()
                             .RunAsync(ctx, http, Operations.Get, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<GetResult<TDetail>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                return GetResult<TDetail>.Blocked;
        }

        switch (await Advisor.For<IResourceGetRequestAdvisor<TEntity>>()
                             .RunAsync(ctx, new() { Name = entity.Name }, http, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<GetResult<TDetail>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                return GetResult<TDetail>.Blocked;
        }

        var detail = _mapper.Map<TEntity, TDetail>(entity);

        switch (await Advisor.For<IResourceResponseAdvisor<TEntity, TDetail>>()
                             .RunAsync(ctx, entity, detail, http, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<GetResult<TDetail>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                return GetResult<TDetail>.Blocked;
        }

        return new() { Detail = detail };
    }

    /// <summary>
    /// Creates a new entity from the request through the full advisor pipeline.
    /// </summary>
    /// <param name="request">The creation request DTO.</param>
    /// <param name="http">The optional HTTP context for route value extraction.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The create result containing the new entity's detail DTO.</returns>
    public async Task<CreateResult<TDetail>> CreateAsync(TRequest request, HttpContext? http, CancellationToken? ct) {
        ct ??= CancellationToken.None;

        var ctx = CreateAdviceContext();

        switch (await Advisor.For<IResourceRequestAdvisor<TEntity>>()
                             .RunAsync(ctx, http, Operations.Create, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<CreateResult<TDetail>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                return CreateResult<TDetail>.Blocked;
        }

        request.Name          = null;
        request.CanonicalName = null;

        if (request is IIdentifier requestId) {
            requestId.Id = default;
        }

        switch (await Advisor.For<IResourceCreateRequestAdvisor<TEntity, TRequest>>()
                             .RunAsync(ctx, request, http, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<CreateResult<TDetail>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                return CreateResult<TDetail>.Blocked;
        }

        var entity = _mapper.Map<TRequest, TEntity>(request);
        if (entity is null) {
            throw new InvalidArgumentException(message: SchemataResources.GetResourceString(SchemataResources.ST1012)) {
                Details = [new BadRequestDetail {
                    FieldViolations = [new() {
                        Field       = "request",
                        Description = SchemataResources.GetResourceString(SchemataResources.ST1012),
                        Reason      = FieldReasons.InvalidPayload,
                    }],
                }],
            };
        }

        if (http is not null) {
            ResourceNameDescriptor.ForType<TEntity>().SetParentFromRouteValues(entity, http.Request.RouteValues);
        }

        switch (await Advisor.For<IResourceCreateAdvisor<TEntity, TRequest>>()
                             .RunAsync(ctx, request, entity, http, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<CreateResult<TDetail>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                return CreateResult<TDetail>.Blocked;
        }

        await _repository.AddAsync(entity, ct.Value);
        await _repository.CommitAsync(ct.Value);

        var detail = _mapper.Map<TEntity, TDetail>(entity);

        switch (await Advisor.For<IResourceResponseAdvisor<TEntity, TDetail>>()
                             .RunAsync(ctx, entity, detail, http, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<CreateResult<TDetail>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                return CreateResult<TDetail>.Blocked;
        }

        return new() { Detail = detail };
    }

    /// <summary>
    /// Updates an existing entity from the request through the full advisor pipeline.
    /// </summary>
    /// <param name="request">The update request DTO.</param>
    /// <param name="entity">The existing entity to update.</param>
    /// <param name="http">The optional HTTP context.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The update result containing the updated detail DTO.</returns>
    public async Task<UpdateResult<TDetail>> UpdateAsync(
        TRequest           request,
        TEntity            entity,
        HttpContext?       http,
        CancellationToken? ct
    ) {
        ct ??= CancellationToken.None;

        var ctx = CreateAdviceContext();

        switch (await Advisor.For<IResourceRequestAdvisor<TEntity>>()
                             .RunAsync(ctx, http, Operations.Update, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<UpdateResult<TDetail>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                return UpdateResult<TDetail>.Blocked;
        }

        switch (await Advisor.For<IResourceUpdateRequestAdvisor<TEntity, TRequest>>()
                             .RunAsync(ctx, request, http, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<UpdateResult<TDetail>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                return UpdateResult<TDetail>.Blocked;
        }

        switch (await Advisor.For<IResourceUpdateAdvisor<TEntity, TRequest>>()
                             .RunAsync(ctx, request, entity, http, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<UpdateResult<TDetail>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                return UpdateResult<TDetail>.Blocked;
        }

        request.Name          = null;
        request.CanonicalName = null;

        ResourceNameDescriptor.ForType<TEntity>().ClearParentProperties(request);

        if (request is IIdentifier requestId) {
            requestId.Id = default;
        }

        if (request is IUpdateMask { UpdateMask: { } mask }) {
            var properties = AppDomainTypeCache.GetProperties(typeof(TEntity));
            var fields     = mask.Split(',').Select(f => f.Trim().Pascalize()).Where(f => properties.ContainsKey(f));
            _mapper.Map(request, entity, fields);
        } else {
            _mapper.Map(request, entity);
        }

        await _repository.UpdateAsync(entity, ct.Value);
        await _repository.CommitAsync(ct.Value);

        var detail = _mapper.Map<TEntity, TDetail>(entity);

        switch (await Advisor.For<IResourceResponseAdvisor<TEntity, TDetail>>()
                             .RunAsync(ctx, entity, detail, http, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<UpdateResult<TDetail>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                return UpdateResult<TDetail>.Blocked;
        }

        return new() { Detail = detail };
    }

    /// <summary>
    /// Deletes an entity through the full advisor pipeline.
    /// </summary>
    /// <param name="entity">The entity to delete.</param>
    /// <param name="etag">The optional ETag for concurrency checking.</param>
    /// <param name="force">Whether to bypass the freshness check.</param>
    /// <param name="http">The optional HTTP context.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns><see langword="true"/> if the entity was deleted or the operation was handled; <see langword="false"/> if blocked.</returns>
    public async Task<bool> DeleteAsync(
        TEntity            entity,
        string?            etag,
        bool               force,
        HttpContext?       http,
        CancellationToken? ct
    ) {
        ct ??= CancellationToken.None;

        var ctx = CreateAdviceContext();

        switch (await Advisor.For<IResourceRequestAdvisor<TEntity>>()
                             .RunAsync(ctx, http, Operations.Delete, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle:
                return true;
            case AdviseResult.Block:
            default:
                return false;
        }

        var req = new DeleteRequest {
            Name = entity.Name, Etag = etag, Force = force,
        };

        switch (await Advisor.For<IResourceDeleteRequestAdvisor<TEntity>>()
                             .RunAsync(ctx, req, http, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle:
                return true;
            case AdviseResult.Block:
            default:
                return false;
        }

        switch (await Advisor.For<IResourceDeleteAdvisor<TEntity>>()
                             .RunAsync(ctx, entity, req, http, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle:
                return true;
            case AdviseResult.Block:
            default:
                return false;
        }

        await _repository.RemoveAsync(entity, ct.Value);
        await _repository.CommitAsync(ct.Value);

        return true;
    }

    private static NotFoundException ResourceNotFound(string? name) {
        var descriptor = ResourceNameDescriptor.ForType<TEntity>();
        return new(message: string.Format(SchemataResources.GetResourceString(SchemataResources.ST1014), name)) {
            Details = [new ResourceInfoDetail { ResourceType = descriptor.Singular, ResourceName = name }],
        };
    }

    private AdviceContext CreateAdviceContext() {
        var ctx = new AdviceContext(_sp);

        var options = _sp.GetService<IOptions<SchemataResourceOptions>>()?.Value;
        if (options is null) {
            return ctx;
        }

        if (options.SuppressCreateValidation) {
            ctx.Set<SuppressCreateRequestValidation>(null);
        }

        if (options.SuppressUpdateValidation) {
            ctx.Set<SuppressUpdateRequestValidation>(null);
        }

        if (options.SuppressFreshness) {
            ctx.Set<SuppressFreshness>(null);
        }

        return ctx;
    }

    private static Expression<Func<TEntity, bool>> BuildNamePredicate(string name) {
        var parameter = Expression.Parameter(typeof(TEntity), "e");
        var property  = Expression.Property(parameter, nameof(ICanonicalName.Name));
        var value     = Expression.Constant(name, typeof(string));
        return Expression.Lambda<Func<TEntity, bool>>(Expression.Equal(property, value), parameter);
    }
}
