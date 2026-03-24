using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
/// Advises on a Create request before the entity is mapped from the request.
/// </summary>
/// <typeparam name="TEntity">The entity type being created.</typeparam>
/// <typeparam name="TRequest">The request DTO type carrying creation data.</typeparam>
/// <remarks>
/// Invoked during <see cref="ResourceOperationHandler{TEntity, TRequest, TDetail, TSummary}.CreateAsync">CreateAsync</see> after <see cref="IResourceRequestAdvisor{TEntity}"/> and before
/// the request is mapped to an entity. Used for authorization, idempotency, and validation.
/// Return <see cref="AdviseResult.Continue"/> to proceed, <see cref="AdviseResult.Handle"/> to return a
/// pre-built result (e.g. cached idempotent response), or <see cref="AdviseResult.Block"/> to deny.
/// </remarks>
public interface IResourceCreateRequestAdvisor<TEntity, TRequest> : IAdvisor<TRequest, HttpContext?>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName;
