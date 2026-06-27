using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Managers;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Identity;

/// <summary>
///     Resolves an OAuth/OIDC subject identifier (`sub`) back to the owning
///     <typeparamref name="TUser" /> and produces the user's claims.
/// </summary>
/// <remarks>
///     <para>
///         Accepts <c>subject</c> in either AIP-122 canonical form (<c>"users/{uid}"</c>)
///         emitted by <c>SchemataUserClaimsPrincipalFactory</c>, or as the bare uid
///         string. The leaf segment after the last <c>'/'</c> is used as the lookup key
///         against <c>SchemataUserManager.FindByIdAsync</c>.
///     </para>
///     <para>
///         Emits <c>sub</c> as the resolved user's <c>CanonicalName</c> so downstream
///         claim assembly and pairwise projection see canonical form; pairwise hashing
///         happens later in the OIDC wire pipeline.
///     </para>
/// </remarks>
internal sealed class IdentitySubjectProvider<TUser>(SchemataUserManager<TUser> manager) : ISubjectProvider
    where TUser : SchemataUser
{
    #region ISubjectProvider Members

    public async Task<IEnumerable<Claim>> GetClaimsAsync(string subject, CancellationToken ct = default) {
        var user = await ResolveAsync(subject);
        if (user is null) {
            return [];
        }

        var canonical = !string.IsNullOrWhiteSpace(user.CanonicalName)
            ? user.CanonicalName!
            : $"users/{user.Uid}";

        var claims = new List<Claim> {
            new(Claims.Subject, canonical),
        };

        var username = await manager.GetUserPrincipalNameAsync(user);
        if (!string.IsNullOrWhiteSpace(username)) {
            claims.Add(new(Claims.PreferredUsername, username));
        }

        var email = await manager.GetEmailAsync(user);
        if (!string.IsNullOrWhiteSpace(email)) {
            claims.Add(new(Claims.Email, email));
            claims.Add(new(Claims.EmailVerified, (await manager.IsEmailConfirmedAsync(user)).ToString().ToLowerInvariant()));
        }

        var phone = await manager.GetPhoneNumberAsync(user);
        if (!string.IsNullOrWhiteSpace(phone)) {
            claims.Add(new(Claims.PhoneNumber, phone));
            claims.Add(new(Claims.PhoneNumberVerified, (await manager.IsPhoneNumberConfirmedAsync(user)).ToString().ToLowerInvariant()));
        }

        var display = await manager.GetDisplayNameAsync(user);
        if (!string.IsNullOrWhiteSpace(display)) {
            claims.Add(new(Claims.Nickname, display));
        }

        foreach (var role in await manager.GetRolesAsync(user)) {
            claims.Add(new(Claims.Role, role));
        }

        return claims;
    }

    public async Task<bool> ValidateAsync(string subject, CancellationToken ct = default) {
        return await ResolveAsync(subject) is not null;
    }

    #endregion

    /// <summary>
    ///     Resolves a subject string to the owning user. Accepts canonical form
    ///     (<c>"users/{uid}"</c>) or the bare uid; the leaf segment is fed into
    ///     <c>SchemataUserManager.FindByIdAsync</c>.
    /// </summary>
    private Task<TUser?> ResolveAsync(string subject) {
        if (string.IsNullOrWhiteSpace(subject)) {
            return Task.FromResult<TUser?>(null);
        }

        var slash = subject.LastIndexOf('/');
        var leaf  = slash < 0 ? subject : subject[(slash + 1)..];
        return manager.FindByIdAsync(leaf);
    }
}
