using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Identity.Skeleton.Advisors;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Services;
using Schemata.Identity.Skeleton.Stores;

namespace Schemata.Identity.Skeleton.Managers;

/// <summary>
///     User manager with Schemata identity store extensions.
/// </summary>
/// <typeparam name="TUser">The user entity type.</typeparam>
public class SchemataUserManager<TUser> : UserManager<TUser>
    where TUser : SchemataUser
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

    /// <summary>Resolves canonical resource subjects independently of database user identifiers.</summary>
    public override async Task<TUser?> GetUserAsync(ClaimsPrincipal principal) {
        var id = GetUserId(principal);
        if (!string.IsNullOrEmpty(id)
         && ResourceNameDescriptor.ForType<TUser>().ParseCanonicalName(id) is not null) {
            return await FindByCanonicalNameAsync(id);
        }

        return await base.GetUserAsync(principal);
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

    /// <summary>Checks whether administrative policy admits the user to new credentials.</summary>
    public virtual Task<bool> IsAdministrativelyActiveAsync(TUser user, CancellationToken ct = default) {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(user);
        return Task.FromResult(user.AdministrativelyActive);
    }

    /// <summary>Persists administrative admission and rotates the security stamp when it changes.</summary>
    public virtual Task<IdentityResult> SetAdministrativeActiveAsync(
        TUser             user,
        bool              active,
        CancellationToken ct = default
    ) {
        return SetAdministrativeActiveAsync(user, active, null, ct);
    }

    /// <summary>Stages administrative admission in an outer transaction or commits an independent transaction.</summary>
    public virtual async Task<IdentityResult> SetAdministrativeActiveAsync(
        TUser             user,
        bool              active,
        IUnitOfWork?      transaction,
        CancellationToken ct = default
    ) {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(user);
        var store = GetProvisioningStore();
        if (user.AdministrativelyActive == active) {
            return IdentityResult.Success;
        }

        var validation = await ValidateProvisioningUserAsync(user);
        if (!validation.Succeeded) {
            return validation;
        }

        user.AdministrativelyActive = active;
        await RotateSecurityStampAsync(user, ct);
        return await ProvisionAsync(store, user, Operations.Update, transaction,
            (provisioning, unit) => provisioning.UpdateAsync(user, unit, ct), ct);
    }

    /// <summary>Hashes the password and creates a user through the transactional manager pipeline.</summary>
    public override async Task<IdentityResult> CreateAsync(TUser user, string password) {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        var result = await UpdatePasswordHash(user, password, true);
        if (!result.Succeeded) return result;
        return await CreateAsync(user, (IUnitOfWork?)null);
    }

    /// <inheritdoc />
    public override Task<IdentityResult> CreateAsync(TUser user) {
        return CreateAsync(user, (IUnitOfWork?)null);
    }

    /// <inheritdoc />
    protected override Task<IdentityResult> UpdateUserAsync(TUser user) {
        return UpdateAsync(user, (IUnitOfWork?)null);
    }

    /// <summary>Creates a user through the provisioning store during administrative provisioning.</summary>
    public virtual async Task<IdentityResult> CreateAsync(
        TUser             user,
        IUnitOfWork?      transaction,
        CancellationToken ct = default
    ) {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(user);
        var store = GetProvisioningStore();
        await EnsureSecurityStampAsync(user, ct);
        var validation = await ValidateProvisioningUserAsync(user);
        if (!validation.Succeeded) {
            return validation;
        }

        return await ProvisionAsync(store, user, Operations.Create, transaction,
            (provisioning, unit) => provisioning.CreateAsync(user, unit, ct), ct);
    }

    /// <summary>Updates a user through the provisioning store and preserves outer transaction ownership.</summary>
    public virtual async Task<IdentityResult> UpdateAsync(
        TUser             user,
        IUnitOfWork?      transaction,
        CancellationToken ct = default
    ) {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(user);
        var store = GetProvisioningStore();
        var validation = await ValidateProvisioningUserAsync(user);
        if (!validation.Succeeded) {
            return validation;
        }

        return await ProvisionAsync(store, user, Operations.Update, transaction,
            (provisioning, unit) => provisioning.UpdateAsync(user, unit, ct), ct);
    }

    /// <summary>Deletes a user and its dependent rows through the provisioning store.</summary>
    public virtual async Task<IdentityResult> DeleteAsync(
        TUser             user,
        IUnitOfWork?      transaction,
        CancellationToken ct = default
    ) {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(user);
        var store = GetProvisioningStore();
        return await ProvisionAsync(store, user, Operations.Delete, transaction,
            (provisioning, unit) => provisioning.DeleteAsync(user, unit, ct), ct);
    }

    /// <summary>Stages a role membership addition in a caller-owned transaction.</summary>
    public virtual async Task AddToRoleAsync(
        TUser user,
        SchemataRole role,
        IUnitOfWork transaction,
        CancellationToken ct = default
    ) {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(transaction);
        if (Store is not IUserRoleStore<TUser>) {
            throw new NotSupportedException("The user store does not support role membership.");
        }

        var store = GetProvisioningStore();
        await store.AddToRoleAsync(user, role, transaction, ct);
    }

    /// <summary>Stages a role membership removal in a caller-owned transaction.</summary>
    public virtual async Task RemoveFromRoleAsync(
        TUser user,
        SchemataRole role,
        IUnitOfWork transaction,
        CancellationToken ct = default
    ) {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(transaction);
        if (Store is not IUserRoleStore<TUser>) {
            throw new NotSupportedException("The user store does not support role membership.");
        }

        var store = GetProvisioningStore();
        await store.RemoveFromRoleAsync(user, role, transaction, ct);
    }

    /// <inheritdoc />
    public override string? NormalizeEmail(string? email) {
        return email is null ? null : _sp.GetRequiredService<IEmailNormalizer>().NormalizeEmail(email);
    }

    /// <summary>Canonicalizes a complete E.164 phone number through the configured phone normalizer.</summary>
    public virtual string NormalizePhoneNumber(string phone) {
        return _sp.GetRequiredService<IPhoneNumberNormalizer>().NormalizePhoneNumber(phone);
    }

    private void NormalizeProvisioningFields(TUser user) {
        user.NormalizedUserName = NormalizeName(user.UserName);
        user.NormalizedEmail = NormalizeEmail(user.Email);
        if (user.PhoneNumber is null) return;
        try {
            user.PhoneNumber = NormalizePhoneNumber(user.PhoneNumber);
        } catch (FormatException) {
            throw new ValidationException([new ErrorFieldViolation {
                Field = "phone_number",
                Reason = "INVALID_PHONE_NUMBER",
                Description = "The phone number must be a complete E.164 value with at most 15 digits.",
            }]);
        }
    }

    private async Task<IdentityResult> ValidateProvisioningUserAsync(TUser user) {
        var errors = new List<IdentityError>();
        foreach (var validator in UserValidators) {
            var result = await validator.ValidateAsync(this, user);
            if (!result.Succeeded) {
                errors.AddRange(result.Errors);
            }
        }

        return errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed(errors.ToArray());
    }

    private async Task EnsureSecurityStampAsync(TUser user, CancellationToken ct) {
        if (!SupportsUserSecurityStamp || Store is not IUserSecurityStampStore<TUser> stamps) {
            return;
        }

        if (await stamps.GetSecurityStampAsync(user, ct) is null) {
            await stamps.SetSecurityStampAsync(user, Guid.NewGuid().ToString(), ct);
        }
    }

    private async Task RotateSecurityStampAsync(TUser user, CancellationToken ct) {
        if (!SupportsUserSecurityStamp || Store is not IUserSecurityStampStore<TUser> stamps) {
            return;
        }

        await stamps.SetSecurityStampAsync(user, Guid.NewGuid().ToString(), ct);
    }

    private async Task<IdentityResult> ProvisionAsync(
        IUserProvisioningStore<TUser> store,
        TUser user,
        Operations operation,
        IUnitOfWork? transaction,
        Func<IUserProvisioningStore<TUser>, IUnitOfWork, Task<IdentityResult>> write,
        CancellationToken ct
    ) {
        if (transaction is not null) {
            return await ProvisionInTransaction(store, user, operation, transaction, write, ct);
        }

        await using var owned = store.BeginProvisioning();
        var result = await ProvisionInTransaction(store, user, operation, owned, write, ct);
        if (result.Succeeded) {
            await owned.CommitAsync(ct);
        }

        return result;
    }

    private async Task<IdentityResult> ProvisionInTransaction(
        IUserProvisioningStore<TUser> store,
        TUser user,
        Operations operation,
        IUnitOfWork transaction,
        Func<IUserProvisioningStore<TUser>, IUnitOfWork, Task<IdentityResult>> write,
        CancellationToken ct
    ) {
        if (operation is Operations.Create or Operations.Update) {
            NormalizeProvisioningFields(user);
            var errors = new List<ErrorFieldViolation>();
            var context = new AdviceContext(_sp);
            var advisors = _sp.GetService<IEnumerable<IIdentityAliasUniquenessAdvisor<TUser>>>()
                        ?? Enumerable.Empty<IIdentityAliasUniquenessAdvisor<TUser>>();
            foreach (var advisor in advisors.OrderBy(value => value.Order)) {
                var result = await advisor.AdviseAsync(context, operation, user, transaction, errors, ct);
                if (result == AdviseResult.Block) throw new ValidationException(errors);
                if (result == AdviseResult.Handle) break;
            }
        }

        return await write(store, transaction);
    }

    private IUserProvisioningStore<TUser> GetProvisioningStore() {
        if (Store is not IUserProvisioningStore<TUser> store) {
            throw new NotSupportedException("The user store does not support transactional provisioning.");
        }

        return store;
    }

    /// <summary>Finds a user by raw username, returning null when the input is PRECIS-invalid.</summary>
    public override async Task<TUser?> FindByNameAsync(string userName) {
        ThrowIfDisposed();
        try {
            return await base.FindByNameAsync(userName);
        } catch (FormatException) {
            return null;
        }
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

        try {
            phone = NormalizePhoneNumber(phone);
        } catch (FormatException) {
            return null;
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
