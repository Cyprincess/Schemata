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
public sealed class ResourceController<TEntity, TRequest, TDetail, TSummary> : ControllerBase
    where TEntity : class, IIdentifier
    where TRequest : class, IIdentifier
    where TDetail : class, IIdentifier
    where TSummary : class, IIdentifier
{
    private readonly ISimpleMapper        _mapper;
    private readonly IRepository<TEntity> _repository;
    private readonly IServiceProvider     _services;

    public ResourceController(IServiceProvider services, IRepository<TEntity> repository, ISimpleMapper mapper) {
        _services   = services;
        _repository = repository;
        _mapper     = mapper;
    }

    private EmptyResult EmptyResult { get; } = new();

    [HttpGet]
    public async Task<IActionResult> Browse(
        [FromQuery] string? query,
        [FromQuery] long?   cursor,
        [FromQuery] int     size = 10) {
        if (!await Advices<IResourceBreadAdvice<TEntity>>.AdviseAsync(_services, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        if (!await Advices<IResourceBrowseAdvice<TEntity>>.AdviseAsync(_services, query, cursor, size, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        var entities = await _repository.ListAsync(q => q.Select(e => e), HttpContext.RequestAborted)
                                        .ToListAsync(HttpContext.RequestAborted);

        if (!await Advices<IResourceResponsesAdvice<TEntity>>.AdviseAsync(_services, entities, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        var summaries = _mapper.Map<IEnumerable<TEntity>, IEnumerable<TSummary>>(entities);
        return Ok(summaries);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Read(long id) {
        if (!await Advices<IResourceBreadAdvice<TEntity>>.AdviseAsync(_services, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        if (!await Advices<IResourceReadAdvice<TEntity>>.AdviseAsync(_services, id, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        var entity = await _repository.SingleOrDefaultAsync(q => q.Where(e => e.Id == id), HttpContext.RequestAborted);
        if (entity is null) {
            return NotFound();
        }

        if (!await Advices<IResourceResponseAdvice<TEntity>>.AdviseAsync(_services, entity, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        var detail = _mapper.Map<TEntity, TDetail>(entity);

        return Ok(detail);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Edit(long id, [FromBody] TRequest request) {
        if (!await Advices<IResourceBreadAdvice<TEntity>>.AdviseAsync(_services, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        if (id != request.Id) {
            return BadRequest();
        }

        if (!await Advices<IResourceEditAdvice<TEntity, TRequest>>.AdviseAsync(_services, id, request, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        var entity = await _repository.SingleOrDefaultAsync(q => q.Where(e => e.Id == id), HttpContext.RequestAborted);
        if (entity is null) {
            return NotFound();
        }

        _mapper.Map(request, entity);

        await _repository.UpdateAsync(entity, HttpContext.RequestAborted);
        await _repository.CommitAsync(HttpContext.RequestAborted);

        if (!await Advices<IResourceResponseAdvice<TEntity>>.AdviseAsync(_services, entity, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        var detail = _mapper.Map<TEntity, TDetail>(entity);

        return Ok(detail);
    }

    [HttpPost]
    public async Task<IActionResult> Add([FromBody] TRequest request) {
        if (!await Advices<IResourceBreadAdvice<TEntity>>.AdviseAsync(_services, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        request.Id = default;

        if (!await Advices<IResourceAddAdvice<TEntity, TRequest>>.AdviseAsync(_services, request, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        var entity = _mapper.Map<TRequest, TEntity>(request);
        if (entity is null) {
            return BadRequest();
        }

        await _repository.AddAsync(entity, HttpContext.RequestAborted);
        await _repository.CommitAsync(HttpContext.RequestAborted);

        if (!await Advices<IResourceResponseAdvice<TEntity>>.AdviseAsync(_services, entity, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        var detail = _mapper.Map<TEntity, TDetail>(entity);

        return CreatedAtAction(nameof(Read), new { id = entity.Id }, detail);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(long id) {
        if (!await Advices<IResourceBreadAdvice<TEntity>>.AdviseAsync(_services, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        if (!await Advices<IResourceDeleteAdvice<TEntity>>.AdviseAsync(_services, id, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        var entity = await _repository.SingleOrDefaultAsync(q => q.Where(e => e.Id == id), HttpContext.RequestAborted);
        if (entity is null) {
            return NotFound();
        }

        await _repository.RemoveAsync(entity, HttpContext.RequestAborted);
        await _repository.CommitAsync(HttpContext.RequestAborted);

        return NoContent();
    }
}
