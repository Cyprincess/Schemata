using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Humanizer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Parlot;
using Schemata.Abstractions.Advices;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Entity.Repository;
using Schemata.Mapping.Skeleton;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Advices;
using Schemata.Resource.Foundation.Grammars;
using Schemata.Resource.Foundation.Models;

namespace Schemata.Resource.Http;

[ApiController]
[ResourceControllerConvention]
[Route("~/Resources/[controller]")]
public class ResourceController<TEntity, TRequest, TDetail, TSummary> : ControllerBase
    where TEntity : class, IIdentifier
    where TRequest : class, IIdentifier
    where TDetail : class, IIdentifier
    where TSummary : class, IIdentifier
{
    protected readonly ISimpleMapper                 Mapper;
    protected readonly IRepository<TEntity>          Repository;
    protected readonly ResourceJsonSerializerOptions SerializerOptions;
    protected readonly IServiceProvider              ServiceProvider;

    public ResourceController(
        IServiceProvider              sp,
        IRepository<TEntity>          repository,
        ISimpleMapper                 mapper,
        ResourceJsonSerializerOptions serializer) {
        Mapper            = mapper;
        Repository        = repository;
        SerializerOptions = serializer;
        ServiceProvider   = sp;
    }

    protected virtual EmptyResult EmptyResult { get; } = new();

    [HttpGet]
    public virtual async Task<IActionResult> List([FromQuery] ListRequest request) {
        var ctx = new AdviceContext();

        if (!await Advices<IResourceRequestAdvice<TEntity>>.AdviseAsync(ServiceProvider, ctx, HttpContext, Operations.List, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        var container = new ResourceRequestContainer<TEntity>();

        if (!await Advices<IResourceListRequestAdvice<TEntity>>.AdviseAsync(ServiceProvider, ctx, request, container, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        var token = await PageToken.FromStringAsync(request.PageToken)
     ?? new PageToken {
            Filter      = request.Filter,
            OrderBy     = request.OrderBy,
            ShowDeleted = request.ShowDeleted,
        };
        if (token.Filter != request.Filter
         || token.OrderBy != request.OrderBy
         || token.ShowDeleted != request.ShowDeleted) {
            throw new InvalidArgumentException {
                Errors = new() {
                    [nameof(request.PageToken).Underscore()] = "mismatch",
                },
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

        var repository = Repository.Once();

        if (!string.IsNullOrWhiteSpace(request.Filter)) {
            try {
                var filter = Parser.Filter.Parse(request.Filter);
                container.ApplyFiltering(filter);
            } catch (ParseException) {
                throw new InvalidArgumentException {
                    Errors = new() {
                        [nameof(request.Filter).Underscore()] = "invalid",
                    },
                };
            }
        }

        if (!string.IsNullOrWhiteSpace(request.OrderBy)) {
            try {
                var order = Parser.Order.Parse(request.OrderBy);
                container.ApplyOrdering(order);
            } catch (ParseException) {
                throw new InvalidArgumentException {
                    Errors = new() {
                        [nameof(request.OrderBy).Underscore()] = "invalid",
                    },
                };
            }
        }

        if (request.ShowDeleted is true) {
            repository = repository.SuppressQuerySoftDelete();
        }

        var response = new ListResponse<TSummary> {
            TotalSize = await repository.LongCountAsync(q => container.Query(q), HttpContext.RequestAborted),
        };

        container.ApplyPaginating(token);

        var entities = repository.ListAsync(q => container.Query(q), HttpContext.RequestAborted);

        var summaries = await Mapper.EachAsync<TEntity, TSummary>(entities).ToListAsync();

        token.Skip += token.PageSize;

        if (summaries.Count >= token.PageSize) {
            response.NextPageToken = await token.ToStringAsync();
        }

        response.Entities = summaries.ToImmutableList();

        if (!await Advices<IResourceResponsesAdvice<TSummary>>.AdviseAsync(ServiceProvider, ctx, response.Entities, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        return new JsonResult(response, SerializerOptions.Options);
    }

    [HttpGet("{id:long}")]
    public virtual async Task<IActionResult> Get(long id) {
        var ctx = new AdviceContext();

        if (!await Advices<IResourceRequestAdvice<TEntity>>.AdviseAsync(ServiceProvider, ctx, HttpContext, Operations.Get, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        if (!await Advices<IResourceGetRequestAdvice<TEntity>>.AdviseAsync(ServiceProvider, ctx, id, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        var repository = Repository.Once().SuppressQuerySoftDelete();

        var entity = await repository.SingleOrDefaultAsync(q => q.Where(e => e.Id == id), HttpContext.RequestAborted);
        if (entity is null) {
            return NotFound();
        }

        var detail = Mapper.Map<TEntity, TDetail>(entity);

        if (!await Advices<IResourceResponseAdvice<TEntity, TDetail>>.AdviseAsync(ServiceProvider, ctx, entity, detail, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        return new JsonResult(detail, SerializerOptions.Options);
    }

    [HttpPost]
    public virtual async Task<IActionResult> Create([FromBody] TRequest request) {
        var ctx = new AdviceContext();

        if (!await Advices<IResourceRequestAdvice<TEntity>>.AdviseAsync(ServiceProvider, ctx, HttpContext, Operations.Create, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        request.Id = 0;

        if (!await Advices<IResourceCreateRequestAdvice<TEntity, TRequest>>.AdviseAsync(ServiceProvider, ctx, request, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        var entity = Mapper.Map<TRequest, TEntity>(request);
        if (entity is null) {
            return BadRequest();
        }

        if (!await Advices<IResourceCreateAdvice<TEntity, TRequest>>.AdviseAsync(ServiceProvider, ctx, request, entity, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        await Repository.AddAsync(entity, HttpContext.RequestAborted);
        await Repository.CommitAsync(HttpContext.RequestAborted);

        var detail = Mapper.Map<TEntity, TDetail>(entity);

        if (!await Advices<IResourceResponseAdvice<TEntity, TDetail>>.AdviseAsync(ServiceProvider, ctx, entity, detail, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        HttpContext.Response.Headers.Location = Url.Action(nameof(Get), new { id = entity.Id });

        return new JsonResult(detail, SerializerOptions.Options) {
            StatusCode = StatusCodes.Status201Created,
        };
    }

    [HttpPut("{id:long}")]
    public virtual async Task<IActionResult> Update(long id, [FromBody] TRequest request) {
        var ctx = new AdviceContext();

        if (!await Advices<IResourceRequestAdvice<TEntity>>.AdviseAsync(ServiceProvider, ctx, HttpContext, Operations.Update, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        if (id != request.Id) {
            return BadRequest();
        }

        if (!await Advices<IResourceEditRequestAdvice<TEntity, TRequest>>.AdviseAsync(ServiceProvider, ctx, id, request, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        var entity = await Repository.SingleOrDefaultAsync(q => q.Where(e => e.Id == id), HttpContext.RequestAborted);
        if (entity is null) {
            return NotFound();
        }

        if (!await Advices<IResourceEditAdvice<TEntity, TRequest>>.AdviseAsync(ServiceProvider, ctx, id, request, entity, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        Mapper.Map(request, entity);

        await Repository.UpdateAsync(entity, HttpContext.RequestAborted);
        await Repository.CommitAsync(HttpContext.RequestAborted);

        var detail = Mapper.Map<TEntity, TDetail>(entity);

        if (!await Advices<IResourceResponseAdvice<TEntity, TDetail>>.AdviseAsync(ServiceProvider, ctx, entity, detail, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        return new JsonResult(detail, SerializerOptions.Options);
    }

    [HttpDelete("{id:long}")]
    public virtual async Task<IActionResult> Delete(long id) {
        var ctx = new AdviceContext();

        if (!await Advices<IResourceRequestAdvice<TEntity>>.AdviseAsync(ServiceProvider, ctx, HttpContext, Operations.Delete, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        if (!await Advices<IResourceDeleteRequestAdvice<TEntity>>.AdviseAsync(ServiceProvider, ctx, id, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        var entity = await Repository.SingleOrDefaultAsync(q => q.Where(e => e.Id == id), HttpContext.RequestAborted);
        if (entity is null) {
            return NotFound();
        }

        if (!await Advices<IResourceDeleteAdvice<TEntity>>.AdviseAsync(ServiceProvider, ctx, id, entity, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        await Repository.RemoveAsync(entity, HttpContext.RequestAborted);
        await Repository.CommitAsync(HttpContext.RequestAborted);

        return NoContent();
    }
}
