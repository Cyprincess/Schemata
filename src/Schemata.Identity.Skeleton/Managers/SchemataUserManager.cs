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

public class SchemataUserManager<TUser> : UserManager<TUser> where TUser : class
{
    private readonly IServiceProvider _sp;

    public SchemataUserManager(
        IServiceProvider                       sp,
        IUserStore<TUser>                      store,
        IOptions<IdentityOptions>              options,
        IPasswordHasher<TUser>                 passwordHasher,
        IEnumerable<IUserValidator<TUser>>     userValidators,
        IEnumerable<IPasswordValidator<TUser>> passwordValidators,
        ILookupNormalizer                      keyNormalizer,
        IdentityErrorDescriber                 errors,
        ILogger<SchemataUserManager<TUser>>    logger) : base(store,
                                                              options,
                                                              passwordHasher,
                                                              userValidators,
                                                              passwordValidators,
                                                              keyNormalizer,
                                                              errors,
                                                              sp,
                                                              logger) {
        _sp = sp;
    }

    public virtual Task<string?> GetDisplayNameAsync(TUser user) {
        ThrowIfDisposed();
        var store = GetDisplayNameStore();

        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        return store.GetDisplayNameAsync(user, CancellationToken);
    }

    public virtual Task<string?> GetUserPrincipalNameAsync(TUser user) {
        ThrowIfDisposed();
        var store = GetUserPrincipalNameStore();

        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        return store.GetUserPrincipalNameAsync(user, CancellationToken);
    }

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
