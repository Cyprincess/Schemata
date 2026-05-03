using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Entity.Repository;

namespace Schemata.Authorization.Foundation.Managers;

/// <summary>
///     Default implementation of <see cref="IApplicationManager{TApplication}" /> backed by an
///     <see cref="IRepository{TEntity}" /> and an <see cref="IPasswordHasher{TUser}" /> for client secret hashing.
/// </summary>
/// <typeparam name="TApplication">The application entity type, must derive from <see cref="SchemataApplication" />.</typeparam>
/// <remarks>
///     Client secrets are hashed using <see cref="IPasswordHasher{TApplication}" /> before storage. Redirect URI
///     and permission checks use exact-match comparison against the application's configured lists.
/// </remarks>
/// <seealso cref="SchemataScopeManager{TScope}" />
/// <seealso cref="SchemataTokenManager{TToken}" />
public class SchemataApplicationManager<TApplication> : IApplicationManager<TApplication>
    where TApplication : SchemataApplication
{
    private readonly IRepository<TApplication>     _applications;
    private readonly IPasswordHasher<TApplication> _hasher;

    public SchemataApplicationManager(IRepository<TApplication> applications, IPasswordHasher<TApplication> hasher) {
        _applications = applications;
        _hasher       = hasher;
    }

    #region IApplicationManager<TApplication> Members

    /// <inheritdoc />
    public IAsyncEnumerable<TApplication> ListAsync(
        Func<IQueryable<TApplication>, IQueryable<TApplication>>? predicate,
        CancellationToken                                         ct = default
    ) {
        return _applications.ListAsync(predicate, ct);
    }

    /// <inheritdoc />
    public async Task<TApplication?> FindByCanonicalNameAsync(string? name, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(name)) {
            return null;
        }

        return await _applications.SingleOrDefaultAsync(q => q.Where(a => a.Name == name), ct);
    }

    /// <inheritdoc />
    public Task<bool> ValidateClientSecretAsync(
        TApplication?     application,
        string?           secret,
        CancellationToken ct = default
    ) {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(application?.ClientSecret)) {
            return Task.FromResult(false);
        }

        if (string.IsNullOrWhiteSpace(secret)) {
            return Task.FromResult(false);
        }

        var result = _hasher.VerifyHashedPassword(application, application.ClientSecret, secret);

        return Task.FromResult(result is PasswordVerificationResult.Success
                                      or PasswordVerificationResult.SuccessRehashNeeded);
    }

    /// <inheritdoc />
    public Task<bool> ValidateRedirectUriAsync(TApplication? application, string? uri, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (application is null) {
            return Task.FromResult(false);
        }

        if (string.IsNullOrWhiteSpace(uri)) {
            return Task.FromResult(false);
        }

        var found = application.RedirectUris?.Any(r => r == uri);

        return Task.FromResult(found == true);
    }

    /// <inheritdoc />
    public Task<bool> ValidatePostLogoutRedirectUriAsync(
        TApplication?     application,
        string?           uri,
        CancellationToken ct = default
    ) {
        ct.ThrowIfCancellationRequested();

        if (application is null) {
            return Task.FromResult(false);
        }

        if (string.IsNullOrWhiteSpace(uri)) {
            return Task.FromResult(false);
        }

        var found = application.PostLogoutRedirectUris?.Any(r => r == uri);

        return Task.FromResult(found == true);
    }

    /// <inheritdoc />
    public Task<bool> HasPermissionAsync(
        TApplication?     application,
        string?           permission,
        CancellationToken ct = default
    ) {
        ct.ThrowIfCancellationRequested();

        if (application is null) {
            return Task.FromResult(false);
        }

        if (string.IsNullOrWhiteSpace(permission)) {
            return Task.FromResult(false);
        }

        var found = application.Permissions?.Any(p => p == permission);

        return Task.FromResult(found == true);
    }

    /// <inheritdoc />
    public Task SetClientSecretAsync(TApplication? application, string? secret, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        application?.ClientSecret = !string.IsNullOrWhiteSpace(secret)
            ? _hasher.HashPassword(application, secret)
            : null;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetDisplayNameAsync(TApplication? application, string? name, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        application?.DisplayName = name;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetDisplayNamesAsync(
        TApplication?               application,
        Dictionary<string, string>? names,
        CancellationToken           ct = default
    ) {
        ct.ThrowIfCancellationRequested();

        application?.DisplayNames = names;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetDescriptionAsync(TApplication? application, string? description, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        application?.Description = description;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetDescriptionsAsync(
        TApplication?               application,
        Dictionary<string, string>? descriptions,
        CancellationToken           ct = default
    ) {
        ct.ThrowIfCancellationRequested();

        application?.Descriptions = descriptions;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetRedirectUrisAsync(
        TApplication?        application,
        ICollection<string>? uris,
        CancellationToken    ct = default
    ) {
        ct.ThrowIfCancellationRequested();

        application?.RedirectUris = uris;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetPostLogoutRedirectUrisAsync(
        TApplication?        application,
        ICollection<string>? uris,
        CancellationToken    ct = default
    ) {
        ct.ThrowIfCancellationRequested();

        application?.PostLogoutRedirectUris = uris;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetPermissionsAsync(
        TApplication?        application,
        ICollection<string>? permissions,
        CancellationToken    ct = default
    ) {
        ct.ThrowIfCancellationRequested();

        application?.Permissions = permissions;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetClientTypeAsync(TApplication? application, string? type, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        application?.ClientType = type;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetApplicationTypeAsync(TApplication? application, string? type, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        application?.ApplicationType = type;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetConsentTypeAsync(TApplication? application, string? type, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        application?.ConsentType = type;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetRequirePkceAsync(TApplication? application, bool? require, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        application?.RequirePkce = require;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetSubjectTypeAsync(TApplication? application, string? type, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        application?.SubjectType = type;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetSectorIdentifierUriAsync(TApplication? application, string? uri, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        application?.SectorIdentifierUri = uri;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetFrontChannelLogoutAsync(
        TApplication?     application,
        string?           uri,
        bool              session,
        CancellationToken ct = default
    ) {
        ct.ThrowIfCancellationRequested();

        if (application is null) {
            return Task.CompletedTask;
        }

        application.FrontChannelLogoutUri             = uri;
        application.FrontChannelLogoutSessionRequired = session;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetBackChannelLogoutAsync(
        TApplication?     application,
        string?           uri,
        bool              session,
        CancellationToken ct = default
    ) {
        ct.ThrowIfCancellationRequested();

        if (application is null) {
            return Task.CompletedTask;
        }

        application.BackChannelLogoutUri             = uri;
        application.BackChannelLogoutSessionRequired = session;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<TApplication?> CreateAsync(TApplication? application, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (application is null) {
            return null;
        }

        await _applications.AddAsync(application, ct);
        await _applications.CommitAsync(ct);

        return application;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(TApplication? application, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (application is null) {
            return;
        }

        await _applications.UpdateAsync(application, ct);
        await _applications.CommitAsync(ct);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(TApplication? application, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (application is null) {
            return;
        }

        await _applications.RemoveAsync(application, ct);
        await _applications.CommitAsync(ct);
    }

    #endregion
}
