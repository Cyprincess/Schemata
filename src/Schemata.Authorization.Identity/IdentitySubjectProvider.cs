using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton;
using Schemata.Common;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Managers;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Identity;

/// <summary>
///     Resolves an OAuth/OIDC subject identifier (`sub`) back to the owning
///     <typeparamref name="TUser" /> and produces the user's claims.
/// </summary>
/// <remarks>
///     <para>
///         Canonical subjects resolve by resource name, including GUID-shaped names.
///         Bare GUID subjects resolve by database identifier.
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

        var canonical = user.CanonicalName
                     ?? throw new InvalidOperationException("The user must have a canonical resource name before issuing claims.");

        var claims = new List<Claim> {
            new(IdentityClaims.Subject, canonical),
        };

        var username = await manager.GetUserPrincipalNameAsync(user);
        if (!string.IsNullOrWhiteSpace(username)) {
            claims.Add(new(IdentityClaims.PreferredUsername, username));
        }

        var email = await manager.GetEmailAsync(user);
        if (!string.IsNullOrWhiteSpace(email)) {
            claims.Add(new(IdentityClaims.Email, email));
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
            claims.Add(new(IdentityClaims.Role, role));
        }

        return claims;
    }

    public async Task<bool> ValidateAsync(string subject, CancellationToken ct = default) {
        return await ResolveAsync(subject) is not null;
    }

    #endregion

    private Task<TUser?> ResolveAsync(string subject) {
        if (string.IsNullOrWhiteSpace(subject)) {
            return Task.FromResult<TUser?>(null);
        }

        if (ResourceNameDescriptor.ForType<TUser>().ParseCanonicalName(subject) is not null) {
            return manager.FindByCanonicalNameAsync(subject);
        }

        return Guid.TryParse(subject, out _)
            ? manager.FindByIdAsync(subject)
            : manager.FindByCanonicalNameAsync(subject);
    }
}
