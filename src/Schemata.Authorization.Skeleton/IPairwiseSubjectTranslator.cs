using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Authorization.Skeleton;

/// <summary>
///     Translates OIDC <c>sub</c> values between the framework-internal AIP-122 canonical
///     form (e.g. <c>"users/{uid}"</c>) and the per-client pairwise hash form, so user-facing
///     resource handlers can stay agnostic of the OIDC pairwise rotation.
/// </summary>
/// <remarks>
///     <para>
///         Resource handlers that expose <c>users</c> (or any resource carrying a
///         <c>[ResourceReference(typeof(SchemataUser))]</c> field) inject this service to:
///     </para>
///     <list type="bullet">
///         <item>
///             <description>
///                 Convert an incoming <c>name</c> / subject parameter from a pairwise client
///                 back to canonical before going to the repository
///                 (<see cref="ToCanonicalAsync" />).
///             </description>
///         </item>
///         <item>
///             <description>
///                 Convert an outgoing canonical subject back to pairwise form for the
///                 caller's client if that client uses
///                 <c>SchemataConstants.SubjectTypes.Pairwise</c> (<see cref="ToPairwiseAsync" />).
///             </description>
///         </item>
///     </list>
///     <para>
///         When the caller is unknown, the calling client is configured for the
///         <c>SubjectTypes.Public</c> subject type, or the subject is empty / already in
///         the requested form, the methods pass through the input value unchanged.
///         Reverse pairwise lookup is best-effort: a value not previously seen by the
///         server in the current process returns <see langword="null" /> rather than
///         attempting to brute-force the hash.
///     </para>
/// </remarks>
public interface IPairwiseSubjectTranslator
{
    /// <summary>
    ///     Translates a wire-side subject identifier to canonical form. Accepts canonical
    ///     input unchanged, recognized pairwise hashes via reverse lookup, and unrecognized
    ///     pairwise hashes by returning <see langword="null" />.
    /// </summary>
    /// <param name="subject">The subject value as received from the client.</param>
    /// <param name="caller">
    ///     The caller's <see cref="ClaimsPrincipal" /> (typically the current HTTP request principal);
    ///     <c>Claims.ClientId</c> on the principal identifies the OAuth client whose
    ///     <c>subject_type</c> drives pairwise selection.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<string?> ToCanonicalAsync(string? subject, ClaimsPrincipal? caller, CancellationToken ct = default);

    /// <summary>
    ///     Translates a canonical subject identifier to wire form: pairwise hash when the
    ///     caller's client is <c>pairwise</c>, otherwise canonical pass-through.
    /// </summary>
    /// <param name="canonicalSubject">The canonical resource name of the subject.</param>
    /// <param name="caller">
    ///     The caller's <see cref="ClaimsPrincipal" /> (typically the current HTTP request principal);
    ///     <c>Claims.ClientId</c> on the principal identifies the OAuth client whose
    ///     <c>subject_type</c> drives pairwise selection.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<string?> ToPairwiseAsync(string? canonicalSubject, ClaimsPrincipal? caller, CancellationToken ct = default);
}
