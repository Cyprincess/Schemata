using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Implements an AIP-136 custom method on a resource. Implementations are
///     declared by <see cref="ResourceMethodAttribute" /> on the resource
///     class and resolved from the DI container at invocation time.
/// </summary>
/// <typeparam name="TEntity">The resource entity type.</typeparam>
/// <typeparam name="TRequest">The custom method's request body type.</typeparam>
/// <typeparam name="TResponse">The custom method's response type.</typeparam>
public interface IResourceMethodHandler<TEntity, in TRequest, TResponse>
    where TEntity : class, ICanonicalName
    where TRequest : class
    where TResponse : class, ICanonicalName
{
    /// <summary>
    ///     Invokes the custom method.
    /// </summary>
    /// <param name="name">
    ///     The resource canonical name for an
    ///     <see cref="ResourceMethodScope.Instance" />-scoped method,
    ///     or <see langword="null" /> for a
    ///     <see cref="ResourceMethodScope.Collection" />-scoped method.
    /// </param>
    /// <param name="request">The incoming request payload.</param>
    /// <param name="entity">
    ///     The resource entity for an
    ///     <see cref="ResourceMethodScope.Instance" />-scoped method,
    ///     or <see langword="null" /> for a
    ///     <see cref="ResourceMethodScope.Collection" />-scoped method.
    /// </param>
    /// <param name="principal">
    ///     The authenticated caller, or <see langword="null" /> when the method
    ///     is invoked anonymously.
    /// </param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The method's response.</returns>
    ValueTask<TResponse> InvokeAsync(
        string?           name,
        TRequest          request,
        TEntity?          entity,
        ClaimsPrincipal?  principal,
        CancellationToken ct
    );
}
