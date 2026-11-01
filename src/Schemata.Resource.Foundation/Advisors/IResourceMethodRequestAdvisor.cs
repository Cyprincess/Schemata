using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Common;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Advises on an AIP-136 custom method request before the instance entity loads and before
///     the registered <see cref="Schemata.Messaging.Skeleton.IRequestHandler{TRequest,TResponse}" />
///     is invoked by the dispatcher. Parallels
///     <see cref="IResourceCreateRequestAdvisor{TEntity,TRequest,TResponse}" />: used by entitlement and
///     access advisors per <seealso href="https://google.aip.dev/136">AIP-136: Custom methods</seealso>.
///     The execution payload carries the verb, target, request, caller, and any handled response;
///     the container carries the composed instance query.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TRequest">The custom method's request DTO type.</typeparam>
/// <typeparam name="TResponse">The custom method's response type.</typeparam>
public interface IResourceMethodRequestAdvisor<TEntity, TRequest, TResponse>
    : IAdvisor<ResourceMethodExecution<TEntity, TRequest, TResponse>, ResourceRequestContainer<TEntity>>
    where TEntity : class, ICanonicalName
    where TRequest : class
    where TResponse : class;
