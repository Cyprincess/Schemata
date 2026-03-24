using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schemata.Identity.Skeleton.Claims;
using Schemata.Identity.Skeleton.Stores;

namespace Schemata.Identity.Skeleton.Managers;

/// <summary>
///     Extended user manager that adds display name, UPN, phone lookup, and claims projection
///     on top of the base ASP.NET Core <see cref="UserManager{TUser}"/>.
/// </summary>
/// <typeparam name="TUser">The user entity type.</typeparam>
public class SchemataUserManager<TUser> : UserManager<TUser>
    where TUser : class
{
    private readonly IServiceProvider _sp;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SchemataUserManager{TUser}"/> class.
    /// </summary>
    public SchemataUserManager(
        IServiceProvider                       sp,
        IUserStore<TUser>                      store,
        IOptions<IdentityOptions>              options,
        IPasswordHasher<TUser>                 passwordHasher,
        IEnumerable<IUserValidator<TUser>>     userValidators,
        IEnumerable<IPasswordValidator<TUser>> passwordValidators,
        ILookupNormalizer                      keyNormalizer,
        IdentityErrorDescriber                 errors,
        ILogger<SchemataUserManager<TUser>>    logger
    ) : base(store, options, passwordHasher, userValidators, passwordValidators, keyNormalizer, errors, sp, logger) {
        _sp = sp;
    }

    /// <summary>
    ///     Gets the display name for the specified user.
    /// </summary>
    /// <param name="user">The user whose display name to retrieve.</param>
    /// <returns>The display name, or <see langword="null"/> if not set.</returns>
    /// <exception cref="NotSupportedException">The store does not implement <see cref="IUserDisplayNameStore{TUser}"/>.</exception>
    public virtual Task<string?> GetDisplayNameAsync(TUser user) {
        ThrowIfDisposed();
        var store = GetDisplayNameStore();

        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        return store.GetDisplayNameAsync(user, CancellationToken);
    }

    /// <summary>
    ///     Gets the user principal name (UPN) for the specified user.
    /// </summary>
    /// <param name="user">The user whose UPN to retrieve.</param>
    /// <returns>The UPN, or <see langword="null"/> if not set.</returns>
    /// <exception cref="NotSupportedException">The store does not implement <see cref="IUserPrincipalNameStore{TUser}"/>.</exception>
    public virtual Task<string?> GetUserPrincipalNameAsync(TUser user) {
        ThrowIfDisposed();
        var store = GetUserPrincipalNameStore();

        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        return store.GetUserPrincipalNameAsync(user, CancellationToken);
    }

    /// <summary>
    ///     Projects the user's identity properties and roles into a <see cref="ClaimsStore"/>.
    /// </summary>
    /// <param name="user">The user to project.</param>
    /// <returns>A claims store containing the user's standard claims and roles.</returns>
    public virtual async Task<ClaimsStore> ToClaimsAsync(TUser user) {
        var claims = new ClaimsStore();

        claims.AddClaim(ClaimTypes.NameIdentifier, await GetUserIdAsync(user));
        claims.AddClaim(ClaimTypes.Upn, await GetUserPrincipalNameAsync(user));
        claims.AddClaim(ClaimTypes.Email, await GetEmailAsync(user));
        claims.AddClaim(ClaimTypes.MobilePhone, await GetPhoneNumberAsync(user));
        claims.AddClaim(ClaimTypes.Name, await GetDisplayNameAsync(user));

        foreach (var role in await GetRolesAsync(user)) {
            claims.AddClaim(ClaimTypes.Role, role);
        }

        return claims;
    }

    /// <summary>
    ///     Finds a user by phone number, including protected data lookup when enabled.
    /// </summary>
    /// <param name="phone">The phone number to search for.</param>
    /// <returns>The user if found; otherwise, <see langword="null"/>.</returns>
    /// <exception cref="NotSupportedException">The store does not implement <see cref="IUserPhoneStore{TUser}"/>.</exception>
    public virtual async Task<TUser?> FindByPhoneAsync(string phone) {
        ThrowIfDisposed();
        var store = GetPhoneNumberStore();

        if (string.IsNullOrWhiteSpace(phone)) {
            throw new ArgumentNullException(nameof(phone));
        }

        var user = await store.FindByPhoneAsync(phone, CancellationToken);

        if (user is not null || !Options.Stores.ProtectPersonalData) {
            return user;
        }

        // Need to potentially check all keys

        var keyring   = _sp.GetService<ILookupProtectorKeyRing>();
        var protector = _sp.GetService<ILookupProtector>();

        if (keyring is null || protector is null) {
            return user;
        }

        foreach (var key in keyring.GetAllKeyIds()) {
            var old = protector.Protect(key, phone);
            user = await store.FindByPhoneAsync(old, CancellationToken);
            if (user is null) {
                continue;
            }

            return user;
        }

        return user;
    }

    private IUserDisplayNameStore<TUser> GetDisplayNameStore() {
        if (Store is not IUserDisplayNameStore<TUser> cast) {
            throw new NotSupportedException();
        }

        return cast;
    }

    private IUserPhoneStore<TUser> GetPhoneNumberStore() {
        if (Store is not IUserPhoneStore<TUser> cast) {
            throw new NotSupportedException();
        }

        return cast;
    }

    private IUserPrincipalNameStore<TUser> GetUserPrincipalNameStore() {
        if (Store is not IUserPrincipalNameStore<TUser> cast) {
            throw new NotSupportedException();
        }

        return cast;
    }
}
