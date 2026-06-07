using System.Security.Claims;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Advises on an AIP-136 custom method dispatch after request-stage advisors
///     have run but before the registered
///     <see cref="Schemata.Abstractions.Resource.IResourceMethodHandler{TEntity, TRequest, TResponse}" />
///     is invoked. Parallels <see cref="IResourceCreateAdvisor{TEntity, TRequest}" /> --
///     advisors can stash a pre-computed response in the
///     <see cref="AdviceContext" /> and return <see cref="AdviseResult.Handle" />
///     to short-circuit the handler invocation entirely.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TRequest">The custom method's request DTO type.</typeparam>
/// <typeparam name="TResponse">The custom method's response type.</typeparam>
public interface IResourceMethodAdvisor<TEntity, TRequest, TResponse> : IAdvisor<TRequest, TEntity, ClaimsPrincipal?>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TResponse : class, ICanonicalName;
