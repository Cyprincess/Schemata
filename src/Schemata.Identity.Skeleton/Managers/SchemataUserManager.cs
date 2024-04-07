using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schemata.Identity.Skeleton.Stores;

namespace Schemata.Identity.Skeleton.Managers;

public class SchemataUserManager<TUser>(
    IUserStore<TUser> store,
    IOptions<IdentityOptions> optionsAccessor,
    IPasswordHasher<TUser> passwordHasher,
    IEnumerable<IUserValidator<TUser>> userValidators,
    IEnumerable<IPasswordValidator<TUser>> passwordValidators,
    ILookupNormalizer keyNormalizer,
    IdentityErrorDescriber errors,
    IServiceProvider services,
    ILogger<SchemataUserManager<TUser>> logger) : UserManager<TUser>(store, optionsAccessor, passwordHasher, userValidators,
    passwordValidators, keyNormalizer, errors, services, logger)
    where TUser : class
{
    private readonly IServiceProvider _services = services;

    public virtual async Task<TUser> FindByPhoneAsync(string phone) {
        ThrowIfDisposed();
        var store = GetPhoneNumberStore();

        if (string.IsNullOrWhiteSpace(phone)) {
            throw new ArgumentNullException(nameof(phone));
        }

        var user = await store.FindByPhoneAsync(phone, CancellationToken).ConfigureAwait(false);

        if (user != null || !Options.Stores.ProtectPersonalData) {
            return user;
        }

        // Need to potentially check all keys

        var keyring   = _services.GetService<ILookupProtectorKeyRing>();
        var protector = _services.GetService<ILookupProtector>();

        if (keyring == null || protector == null) {
            return user;
        }

        foreach (var key in keyring.GetAllKeyIds()) {
            var old = protector.Protect(key, phone);
            user = await store.FindByPhoneAsync(old, CancellationToken).ConfigureAwait(false);
            if (user == null) {
                continue;
            }

            return user;
        }

        return user;
    }

    private IUserPhoneStore<TUser> GetPhoneNumberStore() {
        if (Store is not IUserPhoneStore<TUser> cast) {
            throw new NotSupportedException();
        }

        return cast;
    }
}
