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
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Advice;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Mapping.Skeleton;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Foundation.Grammars;
using Schemata.Resource.Foundation.Models;

namespace Schemata.Resource.Foundation;

public sealed class ResourceOperationHandler<TEntity, TRequest, TDetail, TSummary>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
    where TSummary : class, ICanonicalName
{
    private readonly ISimpleMapper        _mapper;
    private readonly IRepository<TEntity> _repository;
    private readonly IServiceProvider     _sp;

    public ResourceOperationHandler(IServiceProvider sp, IRepository<TEntity> repository, ISimpleMapper mapper) {
        _sp         = sp;
        _repository = repository;
        _mapper     = mapper;
    }

    public async Task<TEntity?> FindByNameAsync(string? name, CancellationToken? ct) {
        if (string.IsNullOrWhiteSpace(name)) {
            throw new InvalidArgumentException(400, "Resource name is required.");
        }

        ct ??= CancellationToken.None;

        var repository = _repository.Once().SuppressQuerySoftDelete();

        return await repository.SingleOrDefaultAsync(q => q.Where(BuildNamePredicate(name)), ct.Value);
    }

    public async Task<TEntity?> FindByCanonicalNameAsync(string? name, CancellationToken? ct) {
        if (string.IsNullOrWhiteSpace(name)) {
            throw new InvalidArgumentException(400, "Resource name is required.");
        }

        ct ??= CancellationToken.None;

        var repository = _repository.Once().SuppressQuerySoftDelete();

        var slash = name.LastIndexOf('/');
        var leaf  = slash >= 0 ? name[(slash + 1)..] : name;

        var entity = await repository.SingleOrDefaultAsync(q => q.Where(BuildNamePredicate(leaf)), ct.Value);

        if (entity?.CanonicalName != name) {
            return null;
        }

        return entity;
    }

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

        var token = await PageToken.FromStringAsync(request.PageToken)
                 ?? new PageToken {
                        Filter = request.Filter, OrderBy = request.OrderBy, ShowDeleted = request.ShowDeleted,
                    };
        if (token.Filter != request.Filter
         || token.OrderBy != request.OrderBy
         || token.ShowDeleted != request.ShowDeleted) {
            throw new InvalidArgumentException {
                Errors = new() { [nameof(request.PageToken).Underscore()] = "invalid" },
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
                throw new InvalidArgumentException {
                    Errors = new() { [nameof(request.Filter).Underscore()] = "invalid" },
                };
            }
        }

        if (!string.IsNullOrWhiteSpace(request.OrderBy)) {
            try {
                var order = Parser.Order.Parse(request.OrderBy);
                container.ApplyOrdering(order);
            } catch (ParseException) {
                throw new InvalidArgumentException {
                    Errors = new() { [nameof(request.OrderBy).Underscore()] = "invalid" },
                };
            }
        }

        if (request.ShowDeleted is true) {
            repository = repository.SuppressQuerySoftDelete();
        }

        var totalSize = await repository.LongCountAsync(q => container.Query(q), ct.Value);

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
            throw new InvalidArgumentException(400, "Invalid request payload.");
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
