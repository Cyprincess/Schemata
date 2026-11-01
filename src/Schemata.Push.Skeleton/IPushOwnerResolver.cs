using System.Security.Claims;

namespace Schemata.Push.Skeleton;

/// <summary>
///     Derives the subscription owner canonical name from the authenticated caller. The Push
///     control surfaces never accept an owner from request input; this resolver is the single
///     source, so Get/List/Delete cannot address another owner through supplied identifiers.
/// </summary>
public interface IPushOwnerResolver
{
    /// <summary>Returns the owner canonical name for <paramref name="principal" />.</summary>
    /// <param name="principal">The authenticated caller, or <see langword="null" /> when anonymous.</param>
    /// <returns>
    ///     The owner canonical name, or <see langword="null" /> when no owner can be derived; the
    ///     control surfaces reject the call in that case.
    /// </returns>
    string? Resolve(ClaimsPrincipal? principal);
}
