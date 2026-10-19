using System.Security.Claims;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Explicit execution payload for one AIP-136 custom method dispatch, owned by the
///     <see cref="IResourceMethodRequestAdvisor{TEntity,TRequest,TResponse}" /> and
///     <see cref="IResourceMethodAdvisor{TEntity,TRequest,TResponse}" /> stages. Advisors read
///     the verb, target, request, loaded entity, and caller from this object, and an advisor
///     that short-circuits writes its response to <see cref="Response" /> before returning
///     <see cref="AdviseResult.Handle" />. The operation handler creates a fresh instance per
///     dispatch, so nested or consecutive invocations never observe a prior request's state.
/// </summary>
/// <typeparam name="TEntity">The resource entity type.</typeparam>
/// <typeparam name="TRequest">The custom method's request DTO type.</typeparam>
/// <typeparam name="TResponse">The custom method's response type.</typeparam>
public sealed class ResourceMethodExecution<TEntity, TRequest, TResponse> : IHandledResponse<TResponse>
    where TEntity : class, ICanonicalName
    where TRequest : class
    where TResponse : class
{
    /// <summary>
    ///     The verb in lowerCamelCase as declared by
    ///     <see cref="Schemata.Abstractions.Resource.ResourceMethodAttribute" />.
    /// </summary>
    public required string Verb { get; init; }

    /// <summary>
    ///     The canonical name of the addressed instance, or <see langword="null" /> for a
    ///     collection-scoped method.
    /// </summary>
    public string? Target { get; init; }

    /// <summary>The incoming request payload.</summary>
    public required TRequest Request { get; init; }

    /// <summary>The authenticated caller, or <see langword="null" /> for anonymous calls.</summary>
    public ClaimsPrincipal? Principal { get; init; }

    /// <summary>The loaded instance entity; <see langword="null" /> until the method stage runs.</summary>
    public TEntity? Entity { get; set; }

    /// <summary>
    ///     The identified dispatch execution attached by the idempotency wrap, or
    ///     <see langword="null" /> when the dispatch runs unidentified. The completion marks on it are
    ///     the accepted evidence of the method's transaction outcome; the wrap never infers one from
    ///     the return value or an exception.
    /// </summary>
    public ResourceDispatchExecution? Dispatch { get; set; }

    /// <summary>The response written by an advisor returning <see cref="AdviseResult.Handle" />.</summary>
    public TResponse? Response { get; set; }

    /// <inheritdoc />
    public bool Handled => Response is not null;

    /// <inheritdoc />
    public void SetResponse(TResponse response) { Response = response; }
}
