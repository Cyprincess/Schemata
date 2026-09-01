using System.Security.Claims;
using Schemata.Abstractions.Entities;

namespace Schemata.Messaging.Skeleton.Commands;

/// <summary>
///     Dispatches an AIP-136 custom method; carries the verb so wrap-position advisors can resolve
///     (operation, TEntity) without reading pipeline state. The inner <typeparamref name="TRequest" />
///     remains the method's own command and keeps its handler pipeline.
/// </summary>
/// <typeparam name="TEntity">The resource entity type the method belongs to.</typeparam>
/// <typeparam name="TRequest">The method's request DTO type.</typeparam>
/// <typeparam name="TResponse">The method's response type.</typeparam>
/// <param name="Verb">The verb in lowerCamelCase as declared by the method's transport surface.</param>
/// <param name="Name">Instance canonical name; <see langword="null" /> for collection-scoped methods.</param>
/// <param name="Request">The method's request payload.</param>
/// <param name="Principal">The authenticated caller, or <see langword="null" /> for anonymous calls.</param>
public sealed record ResourceMethodRequest<TEntity, TRequest, TResponse>(
    string           Verb,
    string?          Name,
    TRequest         Request,
    ClaimsPrincipal? Principal
) : ICommand<TResponse>, IRequestPrincipal
    where TEntity : class, ICanonicalName
    where TRequest : class, IRequest<TResponse>
    where TResponse : class
{
    /// <inheritdoc />
    public ClaimsPrincipal? Principal { get; set; } = Principal;
}
