using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Humanizer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Parlot;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Advice;
using Schemata.Entity.Repository;
using Schemata.Mapping.Skeleton;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Advisors;
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
        ResourceJsonSerializerOptions serializer
    ) {
        Mapper            = mapper;
        Repository        = repository;
        SerializerOptions = serializer;
        ServiceProvider   = sp;
    }

    protected virtual EmptyResult EmptyResult { get; } = new();

    [HttpGet]
    public virtual async Task<IActionResult> List([FromQuery] ListRequest request) {
        var ctx = new AdviceContext(ServiceProvider);

        switch (await Advisor.For<IResourceRequestAdvisor<TEntity>>()
                             .RunAsync(ctx, HttpContext, Operations.List, HttpContext.RequestAborted)) {
            case AdviseResult.Block:
            case AdviseResult.Handle:
                return EmptyResult;
            case AdviseResult.Continue:
                break;
        }

        var container = new ResourceRequestContainer<TEntity>();

        switch (await Advisor.For<IResourceListRequestAdvisor<TEntity>>()
                             .RunAsync(ctx, request, container, HttpContext, HttpContext.RequestAborted)) {
            case AdviseResult.Block:
            case AdviseResult.Handle:
                return EmptyResult;
            case AdviseResult.Continue:
                break;
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

        var repo = Repository.Once();

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
            repo = repo.SuppressQuerySoftDelete();
        }

        var totalSize = await repo.LongCountAsync(q => container.Query(q), HttpContext.RequestAborted);

        container.ApplyPaginating(token);

        var entities = repo.ListAsync(q => container.Query(q), HttpContext.RequestAborted);
        var summaries = await Mapper.EachAsync<TEntity, TSummary>(entities, HttpContext.RequestAborted)
                                    .ToListAsync(HttpContext.RequestAborted);

        token.Skip += token.PageSize;

        string? nextPageToken = null;
        if (summaries.Count >= token.PageSize) {
            nextPageToken = await token.ToStringAsync();
        }

        var immutable = summaries.ToImmutableArray();

        switch (await Advisor.For<IResourceListResponseAdvisor<TSummary>>()
                             .RunAsync(ctx, immutable, HttpContext, HttpContext.RequestAborted)) {
            case AdviseResult.Block:
            case AdviseResult.Handle:
                return EmptyResult;
            case AdviseResult.Continue:
                break;
        }

        return new JsonResult(
            new ListResponse<TSummary> {
                TotalSize = totalSize, Entities = immutable, NextPageToken = nextPageToken,
            }, SerializerOptions.Options);
    }

    [HttpGet("{id:long}")]
    public virtual async Task<IActionResult> Get(long id) {
        var ctx = new AdviceContext(ServiceProvider);

        var repository = Repository.Once().SuppressQuerySoftDelete();

        var entity = await repository.SingleOrDefaultAsync(q => q.Where(e => e.Id == id), HttpContext.RequestAborted);
        if (entity is null) {
            return NotFound();
        }

        switch (await Advisor.For<IResourceRequestAdvisor<TEntity>>()
                             .RunAsync(ctx, HttpContext, Operations.Get, HttpContext.RequestAborted)) {
            case AdviseResult.Block:
            case AdviseResult.Handle:
                return EmptyResult;
            case AdviseResult.Continue:
                break;
        }

        switch (await Advisor.For<IResourceGetRequestAdvisor<TEntity>>()
                             .RunAsync(ctx, id, HttpContext, HttpContext.RequestAborted)) {
            case AdviseResult.Block:
            case AdviseResult.Handle:
                return EmptyResult;
            case AdviseResult.Continue:
                break;
        }

        var detail = Mapper.Map<TEntity, TDetail>(entity);

        switch (await Advisor.For<IResourceResponseAdvisor<TEntity, TDetail>>()
                             .RunAsync(ctx, entity, detail, HttpContext, HttpContext.RequestAborted)) {
            case AdviseResult.Block:
            case AdviseResult.Handle:
                return EmptyResult;
            case AdviseResult.Continue:
                break;
        }

        return new JsonResult(detail, SerializerOptions.Options);
    }

    [HttpPost]
    public virtual async Task<IActionResult> Create([FromBody] TRequest request) {
        var ctx = new AdviceContext(ServiceProvider);

        switch (await Advisor.For<IResourceRequestAdvisor<TEntity>>()
                             .RunAsync(ctx, HttpContext, Operations.Create, HttpContext.RequestAborted)) {
            case AdviseResult.Block:
            case AdviseResult.Handle:
                return EmptyResult;
            case AdviseResult.Continue:
                break;
        }

        request.Id = 0;

        switch (await Advisor.For<IResourceCreateRequestAdvisor<TEntity, TRequest>>()
                             .RunAsync(ctx, request, HttpContext, HttpContext.RequestAborted)) {
            case AdviseResult.Block:
            case AdviseResult.Handle:
                return EmptyResult;
            case AdviseResult.Continue:
                break;
        }

        var entity = Mapper.Map<TRequest, TEntity>(request);
        if (entity is null) {
            throw new InvalidArgumentException(400, "Invalid request payload.");
        }

        switch (await Advisor.For<IResourceCreateAdvisor<TEntity, TRequest>>()
                             .RunAsync(ctx, request, entity, HttpContext, HttpContext.RequestAborted)) {
            case AdviseResult.Block:
            case AdviseResult.Handle:
                return EmptyResult;
            case AdviseResult.Continue:
                break;
        }

        await Repository.AddAsync(entity, HttpContext.RequestAborted);
        await Repository.CommitAsync(HttpContext.RequestAborted);

        var detail = Mapper.Map<TEntity, TDetail>(entity);

        switch (await Advisor.For<IResourceResponseAdvisor<TEntity, TDetail>>()
                             .RunAsync(ctx, entity, detail, HttpContext, HttpContext.RequestAborted)) {
            case AdviseResult.Block:
            case AdviseResult.Handle:
                return EmptyResult;
            case AdviseResult.Continue:
                break;
        }

        HttpContext.Response.Headers.Location = Url.Action(nameof(Get), new { id = entity.Id });

        return new JsonResult(detail, SerializerOptions.Options) { StatusCode = StatusCodes.Status201Created };
    }

    [HttpPut("{id:long}")]
    public virtual async Task<IActionResult> Update(long id, [FromBody] TRequest request) {
        var ctx = new AdviceContext(ServiceProvider);

        switch (await Advisor.For<IResourceRequestAdvisor<TEntity>>()
                             .RunAsync(ctx, HttpContext, Operations.Update, HttpContext.RequestAborted)) {
            case AdviseResult.Block:
            case AdviseResult.Handle:
                return EmptyResult;
            case AdviseResult.Continue:
                break;
        }

        if (id != request.Id) {
            return BadRequest();
        }

        switch (await Advisor.For<IResourceUpdateRequestAdvisor<TEntity, TRequest>>()
                             .RunAsync(ctx, request, HttpContext, HttpContext.RequestAborted)) {
            case AdviseResult.Block:
            case AdviseResult.Handle:
                return EmptyResult;
            case AdviseResult.Continue:
                break;
        }

        var entity = await Repository.SingleOrDefaultAsync(q => q.Where(e => e.Id == id), HttpContext.RequestAborted);
        if (entity is null) {
            return NotFound();
        }

        switch (await Advisor.For<IResourceUpdateAdvisor<TEntity, TRequest>>()
                             .RunAsync(ctx, request, entity, HttpContext, HttpContext.RequestAborted)) {
            case AdviseResult.Block:
            case AdviseResult.Handle:
                return EmptyResult;
            case AdviseResult.Continue:
                break;
        }

        Mapper.Map(request, entity);

        await Repository.UpdateAsync(entity, HttpContext.RequestAborted);
        await Repository.CommitAsync(HttpContext.RequestAborted);

        var detail = Mapper.Map<TEntity, TDetail>(entity);

        switch (await Advisor.For<IResourceResponseAdvisor<TEntity, TDetail>>()
                             .RunAsync(ctx, entity, detail, HttpContext, HttpContext.RequestAborted)) {
            case AdviseResult.Block:
            case AdviseResult.Handle:
                return EmptyResult;
            case AdviseResult.Continue:
                break;
        }

        return new JsonResult(detail, SerializerOptions.Options);
    }

    [HttpDelete("{id:long}")]
    public virtual async Task<IActionResult> Delete(long id) {
        var ctx = new AdviceContext(ServiceProvider);

        switch (await Advisor.For<IResourceRequestAdvisor<TEntity>>()
                             .RunAsync(ctx, HttpContext, Operations.Delete, HttpContext.RequestAborted)) {
            case AdviseResult.Block:
            case AdviseResult.Handle:
                return EmptyResult;
            case AdviseResult.Continue:
                break;
        }

        switch (await Advisor.For<IResourceDeleteRequestAdvisor<TEntity>>()
                             .RunAsync(ctx, id, HttpContext, HttpContext.RequestAborted)) {
            case AdviseResult.Block:
            case AdviseResult.Handle:
                return EmptyResult;
            case AdviseResult.Continue:
                break;
        }

        var entity = await Repository.SingleOrDefaultAsync(q => q.Where(e => e.Id == id), HttpContext.RequestAborted);
        if (entity is null) {
            return NotFound();
        }

        switch (await Advisor.For<IResourceDeleteAdvisor<TEntity>>()
                             .RunAsync(ctx, id, entity, HttpContext, HttpContext.RequestAborted)) {
            case AdviseResult.Block:
            case AdviseResult.Handle:
                return EmptyResult;
            case AdviseResult.Continue:
                break;
        }

        await Repository.RemoveAsync(entity, HttpContext.RequestAborted);
        await Repository.CommitAsync(HttpContext.RequestAborted);

        return NoContent();
    }
}
