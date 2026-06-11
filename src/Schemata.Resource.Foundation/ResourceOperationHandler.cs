using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using Microsoft.AspNetCore.DataProtection;
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
using Schemata.Expressions.Aip;
using Schemata.Expressions.Skeleton;
using Schemata.Mapping.Skeleton;
using Schemata.Resource.Foundation.Advisors;
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
///     step: general request check -> operation-specific request advisor -> entity advisor -> persistence -> response advisor.
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

    private IDataProtector? _protector;

    /// <summary>
    ///     Initializes a new instance with its required dependencies.
    /// </summary>
    /// <param name="sp">The <see cref="IServiceProvider" /> for resolving advisors and options.</param>
    /// <param name="repository">The entity repository.</param>
    /// <param name="mapper">The mapper for entity-DTO conversion.</param>
    public ResourceOperationHandler(IServiceProvider sp, IRepository<TEntity> repository, ISimpleMapper mapper) {
        _sp         = sp;
        _repository = repository;
        _mapper     = mapper;
    }

    private IDataProtector Protector => _protector ??= _sp
        .GetRequiredService<IDataProtectionProvider>()
        .CreateProtector(PageToken.ProtectionPurpose);

    private static void ApplyIdentifierPredicates(ResourceRequestContainer<TEntity> container, string? name) {
        ResourceIdentifiers.Apply(container, name);
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
        StashReadMask(ctx, request.ReadMask);

        switch (await Advisor.For<IResourceRequestAdvisor<TEntity>>()
                             .RunAsync(ctx, principal, nameof(Operations.List), ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<ListResultBase<TSummary>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                throw CollectionNotFound();
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
                throw CollectionNotFound();
        }

        var descriptor = ResourceNameDescriptor.ForType<TEntity>();
        if (!string.IsNullOrWhiteSpace(request.Parent)) {
            var parent = descriptor.ParseParent(request.Parent);
            if (parent is not null) {
                if (parent.Any(kv => kv.Value == "-") && !descriptor.SupportsReadAcross) {
                    throw new ValidationException([new() {
                        Field       = SchemataNaming.ToWireName(nameof(ListRequest.Parent)),
                        Description = SchemataResources.GetResourceString(SchemataResources.ST2002),
                        Reason      = FieldReasons.CrossParentUnsupported,
                    }]);
                }

                var predicate = descriptor.BuildParentPredicate<TEntity>(parent);
                container.ApplyModification(predicate);
            }
        }

        var token = await PageToken.FromStringAsync(request.PageToken, Protector)
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

        if (request.PageSize is < 0) {
            throw new ValidationException([new() {
                Field       = SchemataNaming.ToWireName(nameof(request.PageSize)),
                Description = SchemataResources.GetResourceString(SchemataResources.ST2008),
                Reason      = FieldReasons.InvalidPageSize,
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

        if (!string.IsNullOrWhiteSpace(request.Filter)) {
            try {
                var compiler = _sp.GetRequiredKeyedService<IExpressionCompiler>(AipLanguage.Name);
                var tree = compiler.Parse(request.Filter);
                var filter = compiler.Compile<TEntity, bool>(tree);
                container.ApplyFiltering(filter);
            } catch (Exception ex) when (ex is ParseException or ArgumentException) {
                throw new ValidationException([new() {
                    Field       = SchemataNaming.ToWireName(nameof(request.Filter)),
                    Description = string.Format(SchemataResources.GetResourceString(SchemataResources.ST2004), "filter"),
                    Reason      = FieldReasons.InvalidFilter,
                }]);
            }
        }

        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? order = null;
        if (!string.IsNullOrWhiteSpace(request.OrderBy)) {
            try {
                var compiler = _sp.GetRequiredKeyedService<IOrderCompiler>(AipLanguage.Name);
                order = compiler.CompileOrder<TEntity>(request.OrderBy);
            } catch (Exception ex) when (ex is ParseException or ArgumentException) {
                throw new ValidationException([new() {
                    Field       = SchemataNaming.ToWireName(nameof(request.OrderBy)),
                    Description = string.Format(SchemataResources.GetResourceString(SchemataResources.ST2004), "order_by"),
                    Reason      = FieldReasons.InvalidOrderBy,
                }]);
            }
        }

        container.ApplyOrdering(KeyOrdering<TEntity>.Compose(order));

        using var suppression = request.ShowDeleted is true
            ? _repository.SuppressQuerySoftDelete()
            : null;

        var totalSize = ResolveTotalSizeMode() switch {
            TotalSizeMode.None => (int?)null,
            TotalSizeMode.Estimated => (int)Math.Min(
                await _repository.EstimateCountAsync(q => container.Query(q), ct.Value),
                int.MaxValue),
            var _ => await _repository.CountAsync(q => container.Query(q), ct.Value),
        };

        // The extra look-ahead row detects a following page; AIP-158 forbids a
        // next_page_token when the collection is exhausted, and counting cannot be
        // relied on once total_size becomes optional.
        container.ApplyPaginating(token, 1);

        var entities  = _repository.ListAsync(q => container.Query(q), ct.Value);
        var summaries = await _mapper.EachAsync<TEntity, TSummary>(entities, ct.Value).ToListAsync(ct.Value);

        var hasMore = summaries.Count > token.PageSize;
        if (hasMore) {
            summaries.RemoveAt(summaries.Count - 1);
        }

        token.Skip += token.PageSize;

        string? nextPageToken = null;
        if (hasMore) {
            nextPageToken = await token.ToStringAsync(Protector);
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
                throw CollectionNotFound();
        }

        return new() {
            TotalSize = totalSize,
            Entities = immutable,
            NextPageToken = nextPageToken,
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
    public Task<GetResultBase<TDetail>> GetAsync(string name, ClaimsPrincipal? principal, CancellationToken? ct) {
        return GetAsync(new GetRequest { Name = name }, principal, ct);
    }

    /// <inheritdoc cref="GetAsync(string, ClaimsPrincipal?, CancellationToken?)" />
    /// <param name="request">
    ///     The <see cref="GetRequest" /> carrying the resource name and optional
    ///     <c>read_mask</c> per <seealso href="https://google.aip.dev/157">AIP-157: Partial responses</seealso>.
    /// </param>
    /// <param name="principal">The optional <see cref="ClaimsPrincipal" />.</param>
    /// <param name="ct">The <see cref="CancellationToken" />.</param>
    public async Task<GetResultBase<TDetail>> GetAsync(
        GetRequest         request,
        ClaimsPrincipal?   principal,
        CancellationToken? ct
    ) {
        ct ??= CancellationToken.None;

        var name = request.CanonicalName ?? request.Name ?? string.Empty;

        var ctx = CreateAdviceContext();
        StashReadMask(ctx, request.ReadMask);

        switch (await Advisor.For<IResourceRequestAdvisor<TEntity>>()
                             .RunAsync(ctx, principal, nameof(Operations.Get), ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<GetResultBase<TDetail>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                throw ResourceNotFound(name);
        }

        var container = new ResourceRequestContainer<TEntity>();
        ApplyIdentifierPredicates(container, name);

        switch (await Advisor.For<IResourceGetRequestAdvisor<TEntity>>()
                             .RunAsync(ctx, request, container, principal, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<GetResultBase<TDetail>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                throw ResourceNotFound(name);
        }

        TEntity? entity;
        using (_repository.SuppressQuerySoftDelete()) {
            entity = await _repository.SingleOrDefaultAsync(q => container.Query(q), ct.Value);
        }
        if (entity == null) {
            throw ResourceNotFound(name);
        }

        var detail = _mapper.Map<TEntity, TDetail>(entity);

        switch (await Advisor.For<IResourceResponseAdvisor<TEntity, TDetail>>()
                             .RunAsync(ctx, entity, detail, principal, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<GetResultBase<TDetail>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                throw ResourceNotFound(name);
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
    public Task<CreateResultBase<TDetail>> CreateAsync(
        TRequest           request,
        ClaimsPrincipal?   principal,
        CancellationToken? ct
    ) {
        ct ??= CancellationToken.None;
        var ctx = CreateAdviceContext();
        return CreateCoreAsync(ctx, request, principal, ct.Value, true);
    }

    internal async Task<CreateResultBase<TDetail>> CreateCoreAsync(
        AdviceContext      ctx,
        TRequest           request,
        ClaimsPrincipal?   principal,
        CancellationToken  ct,
        bool               finalize
    ) {
        switch (await Advisor.For<IResourceRequestAdvisor<TEntity>>()
                             .RunAsync(ctx, principal, nameof(Operations.Create), ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<CreateResultBase<TDetail>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                throw CollectionNotFound();
        }

        var container = new ResourceRequestContainer<TEntity>();

        switch (await Advisor.For<IResourceCreateRequestAdvisor<TEntity, TRequest>>()
                             .RunAsync(ctx, request, container, principal, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<CreateResultBase<TDetail>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                throw CollectionNotFound();
        }

        var entity = _mapper.Map<TRequest, TEntity>(request);
        if (entity is null) {
            throw new ValidationException([new() {
                Field       = nameof(request),
                Description = SchemataResources.GetResourceString(SchemataResources.ST2001),
                Reason      = FieldReasons.InvalidPayload,
            }]);
        }

        switch (await Advisor.For<IResourceCreateAdvisor<TEntity, TRequest>>()
                             .RunAsync(ctx, request, entity, principal, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<CreateResultBase<TDetail>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                throw CollectionNotFound();
        }

        await _repository.AddAsync(entity, ct);

        if (!finalize) {
            var staged = _mapper.Map<TEntity, TDetail>(entity);
            return new() { Detail = staged };
        }

        await _repository.CommitAsync(ct);

        var detail = _mapper.Map<TEntity, TDetail>(entity);

        switch (await Advisor.For<IResourceResponseAdvisor<TEntity, TDetail>>()
                             .RunAsync(ctx, entity, detail, principal, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<CreateResultBase<TDetail>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                throw CollectionNotFound();
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
    public Task<UpdateResultBase<TDetail>> UpdateAsync(
        string             name,
        TRequest           request,
        ClaimsPrincipal?   principal,
        CancellationToken? ct
    ) {
        ct ??= CancellationToken.None;
        var ctx = CreateAdviceContext();
        return UpdateCoreAsync(ctx, name, request, principal, ct.Value, true);
    }

    internal async Task<UpdateResultBase<TDetail>> UpdateCoreAsync(
        AdviceContext      ctx,
        string             name,
        TRequest           request,
        ClaimsPrincipal?   principal,
        CancellationToken  ct,
        bool               finalize
    ) {
        switch (await Advisor.For<IResourceRequestAdvisor<TEntity>>()
                             .RunAsync(ctx, principal, nameof(Operations.Update), ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<UpdateResultBase<TDetail>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                throw ResourceNotFound(name);
        }

        ResourceNameDescriptor.ForType<TEntity>().ClearParentProperties(request);

        var container = new ResourceRequestContainer<TEntity>();
        ApplyIdentifierPredicates(container, name);

        switch (await Advisor.For<IResourceUpdateRequestAdvisor<TEntity, TRequest>>()
                             .RunAsync(ctx, request, container, principal, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<UpdateResultBase<TDetail>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                throw ResourceNotFound(name);
        }

        TEntity? entity;
        using (_repository.SuppressQuerySoftDelete()) {
            entity = await _repository.SingleOrDefaultAsync(q => container.Query(q), ct);
        }
        if (entity == null) {
            if (request is IAllowMissing { AllowMissing: true }) {
                return await CreateMissingAsync(ctx, name, request, principal, ct, finalize);
            }

            throw ResourceNotFound(name);
        }

        switch (await Advisor.For<IResourceUpdateAdvisor<TEntity, TRequest>>()
                             .RunAsync(ctx, request, entity, principal, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<UpdateResultBase<TDetail>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                throw ResourceNotFound(name);
        }

        var mask = (request as IUpdateMask)?.UpdateMask;
        if (mask is null || mask.Trim() == Wildcards.Any) {
            _mapper.Map(request, entity);
        } else {
            _mapper.Map(request, entity, ResolveMaskFields(mask));
        }

        await _repository.UpdateAsync(entity, ct);

        if (!finalize) {
            var staged = _mapper.Map<TEntity, TDetail>(entity);
            return new() { Detail = staged };
        }

        await _repository.CommitAsync(ct);

        var detail = _mapper.Map<TEntity, TDetail>(entity);

        switch (await Advisor.For<IResourceResponseAdvisor<TEntity, TDetail>>()
                             .RunAsync(ctx, entity, detail, principal, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<UpdateResultBase<TDetail>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                throw ResourceNotFound(name);
        }

        return new() { Detail = detail };
    }

    /// <summary>
    ///     Creates the resource addressed by an update whose target does not exist,
    ///     per <seealso href="https://google.aip.dev/134">AIP-134</seealso> <c>allow_missing</c>:
    ///     create-stage advisors run, every field applies, the update mask is ignored, and
    ///     the resource name comes from the request URI.
    /// </summary>
    private async Task<UpdateResultBase<TDetail>> CreateMissingAsync(
        AdviceContext     ctx,
        string            name,
        TRequest          request,
        ClaimsPrincipal?  principal,
        CancellationToken ct,
        bool              finalize
    ) {
        var container = new ResourceRequestContainer<TEntity>();

        switch (await Advisor.For<IResourceCreateRequestAdvisor<TEntity, TRequest>>()
                             .RunAsync(ctx, request, container, principal, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<UpdateResultBase<TDetail>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                throw ResourceNotFound(name);
        }

        var entity = _mapper.Map<TRequest, TEntity>(request);
        if (entity is null) {
            throw new ValidationException([new() {
                Field       = nameof(request),
                Description = SchemataResources.GetResourceString(SchemataResources.ST2001),
                Reason      = FieldReasons.InvalidPayload,
            }]);
        }

        var descriptor = ResourceNameDescriptor.ForType<TEntity>();
        var parsed     = descriptor.ParseCanonicalName(name);
        if (parsed is not null) {
            var (parents, leaf) = parsed.Value;
            descriptor.SetParentFromRouteValues(
                entity,
                parents.ToDictionary(kv => kv.Key, kv => (object?)kv.Value));
            entity.Name          = leaf;
            entity.CanonicalName = name;
        }

        switch (await Advisor.For<IResourceCreateAdvisor<TEntity, TRequest>>()
                             .RunAsync(ctx, request, entity, principal, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<UpdateResultBase<TDetail>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                throw ResourceNotFound(name);
        }

        await _repository.AddAsync(entity, ct);

        if (!finalize) {
            var staged = _mapper.Map<TEntity, TDetail>(entity);
            return new() { Detail = staged };
        }

        await _repository.CommitAsync(ct);

        var detail = _mapper.Map<TEntity, TDetail>(entity);

        switch (await Advisor.For<IResourceResponseAdvisor<TEntity, TDetail>>()
                             .RunAsync(ctx, entity, detail, principal, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<UpdateResultBase<TDetail>>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                throw ResourceNotFound(name);
        }

        return new() { Detail = detail };
    }

    /// <summary>
    ///     Deletes a resource
    ///     per <seealso href="https://google.aip.dev/135">AIP-135: Standard methods: Delete</seealso> through the full advisor
    ///     pipeline.
    ///     Authorization is checked before the entity is loaded
    ///     per <seealso href="https://google.aip.dev/211">AIP-211: Authorization checks</seealso>.
    ///     A soft delete returns the updated resource
    ///     per <seealso href="https://google.aip.dev/164">AIP-164: Soft delete</seealso>.
    /// </summary>
    /// <param name="name">The resource name.</param>
    /// <param name="etag">
    ///     The optional ETag for optimistic concurrency
    ///     per <seealso href="https://google.aip.dev/154">AIP-154: Resource freshness validation</seealso>.
    /// </param>
    /// <param name="principal">The optional <see cref="ClaimsPrincipal" />.</param>
    /// <param name="ct">The <see cref="CancellationToken" />.</param>
    /// <returns>
    ///     A <see cref="DeleteResultBase{TDetail}" /> carrying the soft-deleted resource detail,
    ///     or an empty result for a hard delete.
    /// </returns>
    public async Task<DeleteResultBase<TDetail>> DeleteAsync(
        string             name,
        string?            etag,
        ClaimsPrincipal?   principal,
        CancellationToken? ct
    ) {
        var (result, _) = await DeleteAsync(name, etag, principal, ct, true);
        return result;
    }

    internal async Task<(DeleteResultBase<TDetail> Result, TEntity? Entity)> DeleteAsync(
        string             name,
        string?            etag,
        ClaimsPrincipal?   principal,
        CancellationToken? ct,
        bool               finalize
    ) {
        ct ??= CancellationToken.None;

        var ctx = CreateAdviceContext();

        switch (await Advisor.For<IResourceRequestAdvisor<TEntity>>()
                             .RunAsync(ctx, principal, nameof(Operations.Delete), ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<DeleteResultBase<TDetail>>(out var result):
                return (result!, null);
            case AdviseResult.Handle:
                return (new(), null);
            case AdviseResult.Block:
            default:
                throw ResourceNotFound(name);
        }

        var req = new DeleteRequest {
            Name = name, Etag = etag,
        };

        var container = new ResourceRequestContainer<TEntity>();
        ApplyIdentifierPredicates(container, name);

        switch (await Advisor.For<IResourceDeleteRequestAdvisor<TEntity>>()
                             .RunAsync(ctx, req, container, principal, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<DeleteResultBase<TDetail>>(out var result):
                return (result!, null);
            case AdviseResult.Handle:
                return (new(), null);
            case AdviseResult.Block:
            default:
                throw ResourceNotFound(name);
        }

        TEntity? entity;
        using (_repository.SuppressQuerySoftDelete()) {
            entity = await _repository.SingleOrDefaultAsync(q => container.Query(q), ct.Value);
        }
        if (entity == null) {
            throw ResourceNotFound(name);
        }

        switch (await Advisor.For<IResourceDeleteAdvisor<TEntity>>()
                             .RunAsync(ctx, req, entity, principal, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<DeleteResultBase<TDetail>>(out var result):
                return (result!, entity);
            case AdviseResult.Handle:
                return (new(), entity);
            case AdviseResult.Block:
            default:
                throw ResourceNotFound(name);
        }

        await _repository.RemoveAsync(entity, ct.Value);

        if (!finalize) {
            return (new(), entity);
        }

        await _repository.CommitAsync(ct.Value);

        // The remove advisors turn the removal into an update for ISoftDelete entities;
        // a populated DeleteTime after commit identifies the soft path, whose response
        // carries the updated resource per AIP-164.
        if (entity is ISoftDelete { DeleteTime: not null }) {
            var detail = _mapper.Map<TEntity, TDetail>(entity);

            switch (await Advisor.For<IResourceResponseAdvisor<TEntity, TDetail>>()
                                 .RunAsync(ctx, entity, detail, principal, ct.Value)) {
                case AdviseResult.Continue:
                    break;
                case AdviseResult.Handle when ctx.TryGet<DeleteResultBase<TDetail>>(out var result):
                    return (result!, entity);
                case AdviseResult.Block:
                default:
                    throw ResourceNotFound(name);
            }

            return (new() { Detail = detail }, entity);
        }

        return (new(), entity);
    }

    /// <summary>
    ///     Maps AIP-161 field-mask paths to CLR member dot paths. Unknown paths and collection traversal fail
    ///     with <c>INVALID_ARGUMENT</c>; empty segments (paths cleared by
    ///     <see cref="Advisors.AdviceUpdateRequestSanitize" />) are skipped.
    /// </summary>
    private static List<string> ResolveMaskFields(string mask) {
        try {
            return MaskTree.FromWire(typeof(TEntity), mask, false).LeafPaths().ToList();
        } catch (ArgumentException ex) {
            throw InvalidUpdateMaskPath(mask, ex.Message);
        }
    }

    private static ValidationException InvalidUpdateMaskPath(string path, string reason) {
        return new([new() {
            Field       = SchemataNaming.ToWireName(nameof(IUpdateMask.UpdateMask)),
            Description = $"The update_mask path `{path}` is invalid: {reason}.",
            Reason      = FieldReasons.InvalidUpdateMask,
        }]);
    }

    internal static NotFoundException ResourceNotFound(string? name) {
        var descriptor = ResourceNameDescriptor.ForType<TEntity>();
        return new(message: string.Format(SchemataResources.GetResourceString(SchemataResources.ST1011), "Resource", name)) {
            Details = [new ResourceInfoDetail { ResourceType = descriptor.Singular, ResourceName = name }],
        };
    }

    private static NotFoundException CollectionNotFound() {
        return ResourceNotFound(ResourceNameDescriptor.ForType<TEntity>().Collection);
    }

    private static void StashReadMask(AdviceContext ctx, string? mask) {
        if (string.IsNullOrWhiteSpace(mask) || mask.Trim() == Wildcards.Any) {
            return;
        }

        ctx.Set(new ReadMaskRequested(mask));
    }

    private TotalSizeMode ResolveTotalSizeMode() {
        var options = _sp.GetService<IOptions<SchemataResourceOptions>>()?.Value;
        if (options is null) {
            return TotalSizeMode.Exact;
        }

        if (options.Resources.TryGetValue(typeof(TEntity).TypeHandle, out var resource)
         && resource.TotalSize is not TotalSizeMode.Default) {
            return resource.TotalSize;
        }

        return options.TotalSize is TotalSizeMode.Default ? TotalSizeMode.Exact : options.TotalSize;
    }

    private AdviceContext CreateAdviceContext() {
        return ResourceAdviceContext.Create(_sp);
    }
}
