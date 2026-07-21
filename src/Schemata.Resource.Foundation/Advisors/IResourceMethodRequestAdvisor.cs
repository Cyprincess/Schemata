using System.Security.Claims;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Advises on an AIP-136 custom method request before the registered
///     <see cref="Schemata.Abstractions.Resource.IResourceMethodHandler{TEntity, TRequest, TResponse}" />
///     is invoked. Parallels
///     <see cref="IResourceCreateRequestAdvisor{TEntity, TRequest}" /> -- used by
///     sanitize, authorize, validation, and idempotency advisors per
///     <seealso href="https://google.aip.dev/136">AIP-136: Custom methods</seealso>.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TRequest">The custom method's request DTO type.</typeparam>
public interface IResourceMethodRequestAdvisor<TEntity, TRequest> : IAdvisor<TRequest, ResourceRequestContainer<TEntity>, ClaimsPrincipal?>
    where TEntity : class, ICanonicalName
    where TRequest : class;
