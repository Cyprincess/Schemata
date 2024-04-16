using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Humanizer;
using Microsoft.AspNetCore.Mvc;
using Schemata.Abstractions;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Mapping.Skeleton;
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
    protected readonly ISimpleMapper        Mapper;
    protected readonly IRepository<TEntity> Repository;
    protected readonly IServiceProvider     ServiceProvider;

    public ResourceController(IServiceProvider services, IRepository<TEntity> repository, ISimpleMapper mapper) {
        ServiceProvider = services;
        Repository      = repository;
        Mapper          = mapper;
    }

    protected virtual EmptyResult EmptyResult { get; } = new();

    [HttpGet]
    public virtual async Task<IActionResult> List([FromQuery] ListRequest request) {
        if (!await Advices<IResourceRequestAdvice<TEntity>>.AdviseAsync(ServiceProvider, HttpContext, Operations.List, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        if (!await Advices<IResourceListAdvice<TEntity>>.AdviseAsync(ServiceProvider, request, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        Func<IQueryable<TEntity>, IQueryable<TEntity>> query = q => q;

        if (!string.IsNullOrWhiteSpace(request.OrderBy)) {
            var order = Parser.Order.Parse(request.OrderBy);
            query = query.ApplyOrdering(order);
        }

        var entities = await Repository.ListAsync(q => query(q), HttpContext.RequestAborted)
                                       .ToListAsync(HttpContext.RequestAborted);

        if (!await Advices<IResourceResponsesAdvice<TEntity>>.AdviseAsync(ServiceProvider, entities, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        var summaries = Mapper.Map<IEnumerable<TEntity>, IEnumerable<TSummary>>(entities);
        return Ok(summaries);
    }

    [HttpGet("{id}")]
    public virtual async Task<IActionResult> Get(long id) {
        if (!await Advices<IResourceRequestAdvice<TEntity>>.AdviseAsync(ServiceProvider, HttpContext, Operations.Get, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        if (!await Advices<IResourceGetAdvice<TEntity>>.AdviseAsync(ServiceProvider, id, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        var entity = await Repository.SingleOrDefaultAsync(q => q.Where(e => e.Id == id), HttpContext.RequestAborted);
        if (entity is null) {
            return NotFound();
        }

        if (!await Advices<IResourceResponseAdvice<TEntity>>.AdviseAsync(ServiceProvider, entity, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        var detail = Mapper.Map<TEntity, TDetail>(entity);

        return Ok(detail);
    }

    [HttpPost]
    public virtual async Task<IActionResult> Create([FromBody] TRequest request) {
        if (!await Advices<IResourceRequestAdvice<TEntity>>.AdviseAsync(ServiceProvider, HttpContext, Operations.Create, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        request.Id = default;

        if (!await Advices<IResourceCreateAdvice<TEntity, TRequest>>.AdviseAsync(ServiceProvider, request, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        var entity = Mapper.Map<TRequest, TEntity>(request);
        if (entity is null) {
            return BadRequest();
        }

        await Repository.AddAsync(entity, HttpContext.RequestAborted);
        await Repository.CommitAsync(HttpContext.RequestAborted);

        if (!await Advices<IResourceResponseAdvice<TEntity>>.AdviseAsync(ServiceProvider, entity, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        var detail = Mapper.Map<TEntity, TDetail>(entity);

        return CreatedAtAction(nameof(Get), new { id = entity.Id }, detail);
    }

    [HttpPut("{id}")]
    public virtual async Task<IActionResult> Update(long id, [FromBody] TRequest request) {
        if (!await Advices<IResourceRequestAdvice<TEntity>>.AdviseAsync(ServiceProvider, HttpContext, Operations.Update, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        if (id != request.Id) {
            return BadRequest();
        }

        if (!await Advices<IResourceUpdateAdvice<TEntity, TRequest>>.AdviseAsync(ServiceProvider, id, request, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        var entity = await Repository.SingleOrDefaultAsync(q => q.Where(e => e.Id == id), HttpContext.RequestAborted);
        if (entity is null) {
            return NotFound();
        }

        Mapper.Map(request, entity);

        await Repository.UpdateAsync(entity, HttpContext.RequestAborted);
        await Repository.CommitAsync(HttpContext.RequestAborted);

        if (!await Advices<IResourceResponseAdvice<TEntity>>.AdviseAsync(ServiceProvider, entity, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        var detail = Mapper.Map<TEntity, TDetail>(entity);

        return Ok(detail);
    }

    [HttpDelete("{id}")]
    public virtual async Task<IActionResult> Delete(long id) {
        if (!await Advices<IResourceRequestAdvice<TEntity>>.AdviseAsync(ServiceProvider, HttpContext, Operations.Delete, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        if (!await Advices<IResourceDeleteAdvice<TEntity>>.AdviseAsync(ServiceProvider, id, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        var entity = await Repository.SingleOrDefaultAsync(q => q.Where(e => e.Id == id), HttpContext.RequestAborted);
        if (entity is null) {
            return NotFound();
        }

        await Repository.RemoveAsync(entity, HttpContext.RequestAborted);
        await Repository.CommitAsync(HttpContext.RequestAborted);

        return NoContent();
    }
}
