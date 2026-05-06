using System;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
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
///     Orchestrates standard CRUD operations including
///     <seealso href="https://google.aip.dev/131">AIP-131: Standard methods: Get</seealso>,
///     <seealso href="https://google.aip.dev/132">AIP-132: Standard methods: List</seealso>,
///     <seealso href="https://google.aip.dev/133">AIP-133: Standard methods: Create</seealso>,
///     <seealso href="https://google.aip.dev/134">AIP-134: Standard methods: Update</seealso>, and
///     <seealso href="https://google.aip.dev/135">AIP-135: Standard methods: Delete</seealso> by running an advisor
///     pipeline around each
///     step: general request check → operation-specific request advisor → entity advisor → persistence → response advisor.
/// </summary>
/// <typeparam name="TEntity">
///     The persistent entity type implementing <see cref="ICanonicalName" />.
/// </typeparam>
/// <typeparam name="TRequest">The request DTO for create/update operations.</typeparam>
/// <typeparam name="TDetail">The detail DTO returned from get, create, and update.</typeparam>
/// <typeparam name="TSummary">The summary DTO returned from list operations.</typeparam>
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
    ///     Initializes a new instance with its required dependencies.
    /// </summary>
    /// <param name="sp">The <see cref="IServiceProvider" /> for resolving advisors and options.</param>
    /// <param name="repository">The entity repository.</param>
    /// <param name="mapper">The mapper for entity–DTO conversion.</param>
    public ResourceOperationHandler(IServiceProvider sp, IRepository<TEntity> repository, ISimpleMapper mapper) {
        _sp         = sp;
        _repository = repository;
        _mapper     = mapper;
    }

    private void ApplyIdentifierPredicates(ResourceRequestContainer<TEntity> container, string? name) {
        if (string.IsNullOrWhiteSpace(name)) {
            throw new ValidationException([new() {
                Field       = nameof(name),
                Description = string.Format(SchemataResources.GetResourceString(SchemataResources.ST1013), nameof(name).Humanize(LetterCasing.Title)),
                Reason      = FieldReasons.NotEmpty,
            }]);
        }

        var descriptor = ResourceNameDescriptor.ForType<TEntity>();
        var parsed     = descriptor.ParseCanonicalName(name);

        if (parsed is null) {
            throw new ValidationException([new() {
                Field       = nameof(name),
                Description = $"The requested resource name `{name}` is invalid.",
                Reason      = FieldReasons.InvalidName,
            }]);
        }

        var (parents, leaf) = parsed.Value;

        container.ApplyModification(r => r.Name == leaf);

        var parent = descriptor.BuildParentPredicate<TEntity>(parents);
        container.ApplyModification(parent);
    }

    /// <summary>
    ///     Lists resources with filtering
    ///     per <seealso href="https://google.aip.dev/160">AIP-160: Filtering</seealso>, ordering, and pagination
    ///     per <seealso href="https://google.aip.dev/158">AIP-158: Pagination</seealso> through the full advisor pipeline.
    /// </summary>
    /// <param name="request">The list request with filter, order, paging, and parent parameters.</param>
    /// <param name="principal">The optional <see cref="ClaimsPrincipal" />.</param>
    /// <param name="ct">The <see cref="CancellationToken" />.</param>
    /// <returns>A <see cref="ListResultBase{TSummary}" /> with summaries and an optional next page token.</returns>
    public async Task<ListResultBase<TSummary>> ListAsync(
        ListRequest        request,
        ClaimsPrincipal?   principal,
        CancellationToken? ct
    ) {
        ct ??= CancellationToken.None;

        var ctx = CreateAdviceContext();

        switch (await Advisor.For<IResourceRequestAdvisor<TEntity>>()
                             .RunAsync(ctx, principal, Operations.List, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<ListResultBase<TSummary>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                return ListResultBase<TSummary>.Blocked;
        }

        var container = new ResourceRequestContainer<TEntity>();

        switch (await Advisor.For<IResourceListRequestAdvisor<TEntity>>()
                             .RunAsync(ctx, request, container, principal, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<ListResultBase<TSummary>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                return ListResultBase<TSummary>.Blocked;
        }

        var descriptor = ResourceNameDescriptor.ForType<TEntity>();
        if (!string.IsNullOrWhiteSpace(request.Parent)) {
            var parent = descriptor.ParseParent(request.Parent);
            if (parent is not null) {
                if (parent.Any(kv => kv.Value == "-") && !descriptor.SupportsReadAcross) {
                    throw new ValidationException([new() {
                        Field       = "parent",
                        Description = SchemataResources.GetResourceString(SchemataResources.ST2002),
                        Reason      = FieldReasons.CrossParentUnsupported,
                    }]);
                }

                var predicate = descriptor.BuildParentPredicate<TEntity>(parent);
                container.ApplyModification(predicate);
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
            throw new ValidationException([new() {
                Field       = nameof(request.PageToken).Underscore(),
                Description = SchemataResources.GetResourceString(SchemataResources.ST2003),
                Reason      = FieldReasons.InvalidPageToken,
            }]);
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
                throw new ValidationException([new() {
                    Field       = nameof(request.Filter).Underscore(),
                    Description = string.Format(SchemataResources.GetResourceString(SchemataResources.ST2004), "filter"),
                    Reason      = FieldReasons.InvalidFilter,
                }]);
            }
        }

        if (!string.IsNullOrWhiteSpace(request.OrderBy)) {
            try {
                var order = Parser.Order.Parse(request.OrderBy);
                container.ApplyOrdering(order);
            } catch (ParseException) {
                throw new ValidationException([new() {
                    Field       = nameof(request.OrderBy).Underscore(),
                    Description = string.Format(SchemataResources.GetResourceString(SchemataResources.ST2004), "order_by"),
                    Reason      = FieldReasons.InvalidOrderBy,
                }]);
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
                             .RunAsync(ctx, immutable, principal, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<ListResultBase<TSummary>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                return ListResultBase<TSummary>.Blocked;
        }

        return new() {
            TotalSize = totalSize, Entities = immutable, NextPageToken = nextPageToken,
        };
    }

    /// <summary>
    ///     Gets a resource by name
    ///     per <seealso href="https://google.aip.dev/131">AIP-131: Standard methods: Get</seealso> through the advisor
    ///     pipeline.
    ///     Authorization is checked before the entity is loaded
    ///     per <seealso href="https://google.aip.dev/211">AIP-211: Authorization checks</seealso>.
    /// </summary>
    /// <param name="name">The resource name.</param>
    /// <param name="principal">The optional <see cref="ClaimsPrincipal" />.</param>
    /// <param name="ct">The <see cref="CancellationToken" />.</param>
    /// <returns>A <see cref="GetResultBase{TDetail}" /> containing the detail DTO.</returns>
    public async Task<GetResultBase<TDetail>> GetAsync(string name, ClaimsPrincipal? principal, CancellationToken? ct) {
        ct ??= CancellationToken.None;

        var ctx = CreateAdviceContext();

        switch (await Advisor.For<IResourceRequestAdvisor<TEntity>>()
                             .RunAsync(ctx, principal, Operations.Get, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<GetResultBase<TDetail>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                return GetResultBase<TDetail>.Blocked;
        }

        var container = new ResourceRequestContainer<TEntity>();
        ApplyIdentifierPredicates(container, name);

        switch (await Advisor.For<IResourceGetRequestAdvisor<TEntity>>()
                             .RunAsync(ctx, new() { Name = name }, container, principal, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<GetResultBase<TDetail>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                return GetResultBase<TDetail>.Blocked;
        }

        var entity = await _repository.Once()
                                      .SuppressQuerySoftDelete()
                                      .SingleOrDefaultAsync(q => container.Query(q), ct.Value)
                  ?? throw ResourceNotFound(name);

        var detail = _mapper.Map<TEntity, TDetail>(entity);

        switch (await Advisor.For<IResourceResponseAdvisor<TEntity, TDetail>>()
                             .RunAsync(ctx, entity, detail, principal, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<GetResultBase<TDetail>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                return GetResultBase<TDetail>.Blocked;
        }

        return new() { Detail = detail };
    }

    /// <summary>
    ///     Creates a resource
    ///     per <seealso href="https://google.aip.dev/133">AIP-133: Standard methods: Create</seealso> through the full advisor
    ///     pipeline.
    /// </summary>
    /// <param name="request">The creation request DTO.</param>
    /// <param name="principal">The optional <see cref="ClaimsPrincipal" />.</param>
    /// <param name="ct">The <see cref="CancellationToken" />.</param>
    /// <returns>A <see cref="CreateResultBase{TDetail}" /> containing the new resource's detail DTO.</returns>
    public async Task<CreateResultBase<TDetail>> CreateAsync(
        TRequest           request,
        ClaimsPrincipal?   principal,
        CancellationToken? ct
    ) {
        ct ??= CancellationToken.None;

        var ctx = CreateAdviceContext();

        switch (await Advisor.For<IResourceRequestAdvisor<TEntity>>()
                             .RunAsync(ctx, principal, Operations.Create, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<CreateResultBase<TDetail>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                return CreateResultBase<TDetail>.Blocked;
        }

        var container = new ResourceRequestContainer<TEntity>();

        switch (await Advisor.For<IResourceCreateRequestAdvisor<TEntity, TRequest>>()
                             .RunAsync(ctx, request, container, principal, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<CreateResultBase<TDetail>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                return CreateResultBase<TDetail>.Blocked;
        }

        var entity = _mapper.Map<TRequest, TEntity>(request);
        if (entity is null) {
            throw new ValidationException([new() {
                Field       = "request",
                Description = SchemataResources.GetResourceString(SchemataResources.ST2001),
                Reason      = FieldReasons.InvalidPayload,
            }]);
        }

        switch (await Advisor.For<IResourceCreateAdvisor<TEntity, TRequest>>()
                             .RunAsync(ctx, request, entity, principal, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<CreateResultBase<TDetail>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                return CreateResultBase<TDetail>.Blocked;
        }

        await _repository.AddAsync(entity, ct.Value);
        await _repository.CommitAsync(ct.Value);

        var detail = _mapper.Map<TEntity, TDetail>(entity);

        switch (await Advisor.For<IResourceResponseAdvisor<TEntity, TDetail>>()
                             .RunAsync(ctx, entity, detail, principal, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<CreateResultBase<TDetail>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                return CreateResultBase<TDetail>.Blocked;
        }

        return new() { Detail = detail };
    }

    /// <summary>
    ///     Updates a resource
    ///     per <seealso href="https://google.aip.dev/134">AIP-134: Standard methods: Update</seealso> through the full advisor
    ///     pipeline.
    ///     Authorization is checked before the entity is loaded
    ///     per <seealso href="https://google.aip.dev/211">AIP-211: Authorization checks</seealso>.
    ///     Uses field masks
    ///     per <seealso href="https://google.aip.dev/161">AIP-161: Field masks</seealso> when the request implements
    ///     <see cref="IUpdateMask" />.
    /// </summary>
    /// <param name="name">The resource name.</param>
    /// <param name="request">The update request DTO.</param>
    /// <param name="principal">The optional <see cref="ClaimsPrincipal" />.</param>
    /// <param name="ct">The <see cref="CancellationToken" />.</param>
    /// <returns>An <see cref="UpdateResultBase{TDetail}" /> containing the updated detail DTO.</returns>
    public async Task<UpdateResultBase<TDetail>> UpdateAsync(
        string             name,
        TRequest           request,
        ClaimsPrincipal?   principal,
        CancellationToken? ct
    ) {
        ct ??= CancellationToken.None;

        var ctx = CreateAdviceContext();

        switch (await Advisor.For<IResourceRequestAdvisor<TEntity>>()
                             .RunAsync(ctx, principal, Operations.Update, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<UpdateResultBase<TDetail>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                return UpdateResultBase<TDetail>.Blocked;
        }

        ResourceNameDescriptor.ForType<TEntity>().ClearParentProperties(request);

        var container = new ResourceRequestContainer<TEntity>();
        ApplyIdentifierPredicates(container, name);

        switch (await Advisor.For<IResourceUpdateRequestAdvisor<TEntity, TRequest>>()
                             .RunAsync(ctx, request, container, principal, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<UpdateResultBase<TDetail>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                return UpdateResultBase<TDetail>.Blocked;
        }

        var entity = await _repository.Once()
                                      .SuppressQuerySoftDelete()
                                      .SingleOrDefaultAsync(q => container.Query(q), ct.Value)
                  ?? throw ResourceNotFound(name);

        switch (await Advisor.For<IResourceUpdateAdvisor<TEntity, TRequest>>()
                             .RunAsync(ctx, request, entity, principal, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<UpdateResultBase<TDetail>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                return UpdateResultBase<TDetail>.Blocked;
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
                             .RunAsync(ctx, entity, detail, principal, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<UpdateResultBase<TDetail>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                return UpdateResultBase<TDetail>.Blocked;
        }

        return new() { Detail = detail };
    }

    /// <summary>
    ///     Deletes a resource
    ///     per <seealso href="https://google.aip.dev/135">AIP-135: Standard methods: Delete</seealso> through the full advisor
    ///     pipeline.
    ///     Authorization is checked before the entity is loaded
    ///     per <seealso href="https://google.aip.dev/211">AIP-211: Authorization checks</seealso>.
    /// </summary>
    /// <param name="name">The resource name.</param>
    /// <param name="etag">
    ///     The optional ETag for optimistic concurrency
    ///     per <seealso href="https://google.aip.dev/154">AIP-154: Resource freshness validation</seealso>.
    /// </param>
    /// <param name="force">When <see langword="true" />, bypasses the freshness check.</param>
    /// <param name="principal">The optional <see cref="ClaimsPrincipal" />.</param>
    /// <param name="ct">The <see cref="CancellationToken" />.</param>
    /// <returns>
    ///     <see langword="true" /> if deleted or handled; <see langword="false" /> if blocked.
    /// </returns>
    public async Task<bool> DeleteAsync(
        string             name,
        string?            etag,
        bool               force,
        ClaimsPrincipal?   principal,
        CancellationToken? ct
    ) {
        ct ??= CancellationToken.None;

        var ctx = CreateAdviceContext();

        switch (await Advisor.For<IResourceRequestAdvisor<TEntity>>()
                             .RunAsync(ctx, principal, Operations.Delete, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle:
                return true;
            case AdviseResult.Block:
            default:
                return false;
        }

        var req = new DeleteRequest {
            Name = name, Etag = etag, Force = force,
        };

        var container = new ResourceRequestContainer<TEntity>();
        ApplyIdentifierPredicates(container, name);

        switch (await Advisor.For<IResourceDeleteRequestAdvisor<TEntity>>()
                             .RunAsync(ctx, req, container, principal, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle:
                return true;
            case AdviseResult.Block:
            default:
                return false;
        }

        var entity = await _repository.Once()
                                      .SuppressQuerySoftDelete()
                                      .SingleOrDefaultAsync(q => container.Query(q), ct.Value)
                  ?? throw ResourceNotFound(name);

        switch (await Advisor.For<IResourceDeleteAdvisor<TEntity>>()
                             .RunAsync(ctx, entity, req, principal, ct.Value)) {
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
        return new(message: string.Format(SchemataResources.GetResourceString(SchemataResources.ST1011), "Resource", name)) {
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
            ctx.Set<CreateRequestValidationSuppressed>(null);
        }

        if (options.SuppressUpdateValidation) {
            ctx.Set<UpdateRequestValidationSuppressed>(null);
        }

        if (options.SuppressFreshness) {
            ctx.Set<FreshnessSuppressed>(null);
        }

        return ctx;
    }
}
