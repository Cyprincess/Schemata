using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Mapping.Skeleton;
using Schemata.Resource.Foundation.Advices;

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
    public virtual async Task<IActionResult> Browse(
        [FromQuery] string? query,
        [FromQuery] long?   cursor,
        [FromQuery] int     size = 10) {
        if (!await Advices<IResourceBreadAdvice<TEntity>>.AdviseAsync(ServiceProvider, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        if (!await Advices<IResourceBrowseAdvice<TEntity>>.AdviseAsync(ServiceProvider, query, cursor, size, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        var entities = await Repository.ListAsync(q => q.Select(e => e), HttpContext.RequestAborted)
                                        .ToListAsync(HttpContext.RequestAborted);

        if (!await Advices<IResourceResponsesAdvice<TEntity>>.AdviseAsync(ServiceProvider, entities, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        var summaries = Mapper.Map<IEnumerable<TEntity>, IEnumerable<TSummary>>(entities);
        return Ok(summaries);
    }

    [HttpGet("{id}")]
    public virtual async Task<IActionResult> Read(long id) {
        if (!await Advices<IResourceBreadAdvice<TEntity>>.AdviseAsync(ServiceProvider, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        if (!await Advices<IResourceReadAdvice<TEntity>>.AdviseAsync(ServiceProvider, id, HttpContext, HttpContext.RequestAborted)) {
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

    [HttpPut("{id}")]
    public virtual async Task<IActionResult> Edit(long id, [FromBody] TRequest request) {
        if (!await Advices<IResourceBreadAdvice<TEntity>>.AdviseAsync(ServiceProvider, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        if (id != request.Id) {
            return BadRequest();
        }

        if (!await Advices<IResourceEditAdvice<TEntity, TRequest>>.AdviseAsync(ServiceProvider, id, request, HttpContext, HttpContext.RequestAborted)) {
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

    [HttpPost]
    public virtual async Task<IActionResult> Add([FromBody] TRequest request) {
        if (!await Advices<IResourceBreadAdvice<TEntity>>.AdviseAsync(ServiceProvider, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        request.Id = default;

        if (!await Advices<IResourceAddAdvice<TEntity, TRequest>>.AdviseAsync(ServiceProvider, request, HttpContext, HttpContext.RequestAborted)) {
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

        return CreatedAtAction(nameof(Read), new { id = entity.Id }, detail);
    }

    [HttpDelete("{id}")]
    public virtual async Task<IActionResult> Delete(long id) {
        if (!await Advices<IResourceBreadAdvice<TEntity>>.AdviseAsync(ServiceProvider, HttpContext, HttpContext.RequestAborted)) {
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
