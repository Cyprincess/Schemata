using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Advises on an AIP-136 custom method dispatch after request-stage advisors
///     have run, the instance entity has loaded, and before the registered
///     <see cref="Schemata.Messaging.Skeleton.IRequestHandler{TRequest,TResponse}" /> is invoked by
///     the dispatcher. Parallels <see cref="IResourceCreateAdvisor{TEntity, TRequest}" />: an advisor
///     that writes its response to <see cref="ResourceMethodExecution{TEntity,TRequest,TResponse}.Response" />
///     and returns <see cref="AdviseResult.Handle" /> short-circuits the handler invocation entirely.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TRequest">The custom method's request DTO type.</typeparam>
/// <typeparam name="TResponse">The custom method's response type.</typeparam>
public interface IResourceMethodAdvisor<TEntity, TRequest, TResponse>
    : IAdvisor<ResourceMethodExecution<TEntity, TRequest, TResponse>>
    where TEntity : class, ICanonicalName
    where TRequest : class
    where TResponse : class, ICanonicalName;
