using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schemata.Identity.Skeleton.Stores;

namespace Schemata.Identity.Skeleton.Managers;

/// <summary>
///     User manager with Schemata identity store extensions.
/// </summary>
/// <typeparam name="TUser">The user entity type.</typeparam>
public class SchemataUserManager<TUser> : UserManager<TUser>
    where TUser : class
{
    private readonly IServiceProvider _sp;

    /// <summary>
    ///     Initializes a user manager with Schemata store extension support.
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
    ///     Gets the display name for a user.
    /// </summary>
    public virtual Task<string?> GetDisplayNameAsync(TUser user) {
        ThrowIfDisposed();
        var store = GetDisplayNameStore();

        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        return store.GetDisplayNameAsync(user, CancellationToken);
    }

    /// <summary>
    ///     Gets the principal name for a user.
    /// </summary>
    public virtual Task<string?> GetUserPrincipalNameAsync(TUser user) {
        ThrowIfDisposed();
        var store = GetUserPrincipalNameStore();

        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        return store.GetUserPrincipalNameAsync(user, CancellationToken);
    }

    /// <summary>
    ///     Finds a user by canonical resource name.
    /// </summary>
    public virtual Task<TUser?> FindByCanonicalNameAsync(string canonicalName) {
        ThrowIfDisposed();
        var store = GetCanonicalNameStore();

        if (string.IsNullOrWhiteSpace(canonicalName)) {
            throw new ArgumentNullException(nameof(canonicalName));
        }

        return store.FindByCanonicalNameAsync(canonicalName, CancellationToken);
    }

    /// <summary>
    ///     Finds a user by phone number.
    /// </summary>
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

        // Protected personal data can be encrypted with older lookup keys.

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

    private IUserCanonicalNameStore<TUser> GetCanonicalNameStore() {
        if (Store is not IUserCanonicalNameStore<TUser> cast) {
            throw new NotSupportedException();
        }

        return cast;
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
