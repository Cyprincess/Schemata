using System.Security.Claims;
using Schemata.Abstractions.Advisors;

namespace Schemata.Identity.Skeleton;

/// <summary>
///     Explicit execution payload for one identity operation dispatch, carried through every
///     advisor stage of that operation. Advisors read the operation and caller from this object,
///     and an advisor that short-circuits writes its result to <see cref="Response" /> before
///     returning <see cref="AdviseResult.Handle" />. The handler creates a fresh instance per
///     dispatch, so consecutive operations never observe a prior result.
/// </summary>
/// <typeparam name="TResponse">The operation's result payload type.</typeparam>
public sealed class IdentityOperationExecution<TResponse> : IHandledResponse<TResponse> where TResponse : class
{
    /// <summary>The identity operation being dispatched.</summary>
    public required IdentityOperation Operation { get; init; }

    /// <summary>The caller on whose behalf the operation runs.</summary>
    public required ClaimsPrincipal Principal { get; init; }

    /// <summary>The result written by an advisor returning <see cref="AdviseResult.Handle" />.</summary>
    public TResponse? Response { get; set; }

    /// <inheritdoc />
    public bool Handled => Response is not null;

    /// <inheritdoc />
    public void SetResponse(TResponse response) { Response = response; }
}
