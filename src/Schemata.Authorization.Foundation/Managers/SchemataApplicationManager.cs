using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Entity.Repository;
using Schemata.Common;
using Schemata.Common.Errors;
using Schemata.Security.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Managers;

/// <summary>
///     Default implementation of <see cref="IApplicationManager{TApplication}" /> backed by an
///     <see cref="IRepository{TEntity}" /> for reads and the injected
///     <see cref="IResourceMutation{TApplication}" /> owner for writes. Publication fences join the
///     caller's actual transaction and touch the current application through the same owner.
/// </summary>
/// <typeparam name="TApplication">The application entity type, must derive from <see cref="SchemataApplication" />.</typeparam>
/// <typeparam name="TAuthorization">The configured consent entity type checked during token publication.</typeparam>
/// <remarks>
///     Redirect URI and permission checks use exact-match comparison against the application's configured lists.
/// </remarks>
/// <seealso cref="SchemataScopeManager{TScope}" />
public class SchemataApplicationManager<TApplication, TAuthorization> : IApplicationManager<TApplication>
    where TApplication : SchemataApplication
    where TAuthorization : SchemataAuthorization
{
    private readonly IResourceMutation<TApplication> _mutation;
    private readonly IServiceProvider                _services;

    public SchemataApplicationManager(
        IServiceProvider                services,
        IResourceMutation<TApplication> mutation
    ) {
        _services     = services;
        _mutation     = mutation;
    }

    #region IApplicationManager<TApplication> Members

    public async IAsyncEnumerable<TApplication> ListAsync(
        Func<IQueryable<TApplication>, IQueryable<TApplication>>? predicate,
        [EnumeratorCancellation] CancellationToken ct = default
    ) {
        await using var applications = _services.GetRequiredService<IRepository<TApplication>>();
        await foreach (var application in applications.ListAsync(predicate, ct)) {
            yield return application;
        }
    }

    public async Task<TApplication?> FindByClientIdAsync(string? clientId, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(clientId)) {
            return null;
        }

        await using var applications = _services.GetRequiredService<IRepository<TApplication>>();
        return await applications.SingleOrDefaultAsync(q => q.Where(a => a.ClientId == clientId), ct);
    }

    public Task<bool> ValidateRedirectUriAsync(TApplication? application, string? uri, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (application is null) {
            return Task.FromResult(false);
        }

        if (string.IsNullOrWhiteSpace(uri)) {
            return Task.FromResult(false);
        }

        var found = application.RedirectUris?.Any(r => RedirectUriMatcher.Matches(r, uri));

        return Task.FromResult(found == true);
    }

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

    public Task SetClientNameAsync(TApplication? application, string? name, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        application?.ClientName = name;

        return Task.CompletedTask;
    }

    public Task SetDisplayNamesAsync(
        TApplication?                application,
        Dictionary<string, string?>? names,
        CancellationToken            ct = default
    ) {
        ct.ThrowIfCancellationRequested();

        application?.DisplayNames = names;

        return Task.CompletedTask;
    }

    public Task SetDescriptionAsync(TApplication? application, string? description, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        application?.Description = description;

        return Task.CompletedTask;
    }

    public Task SetDescriptionsAsync(
        TApplication?                application,
        Dictionary<string, string?>? descriptions,
        CancellationToken            ct = default
    ) {
        ct.ThrowIfCancellationRequested();

        application?.Descriptions = descriptions;

        return Task.CompletedTask;
    }

    public Task SetRedirectUrisAsync(
        TApplication?        application,
        ICollection<string>? uris,
        CancellationToken    ct = default
    ) {
        ct.ThrowIfCancellationRequested();

        application?.RedirectUris = uris;

        return Task.CompletedTask;
    }

    public Task SetPostLogoutRedirectUrisAsync(
        TApplication?        application,
        ICollection<string>? uris,
        CancellationToken    ct = default
    ) {
        ct.ThrowIfCancellationRequested();

        application?.PostLogoutRedirectUris = uris;

        return Task.CompletedTask;
    }

    public Task SetPermissionsAsync(
        TApplication?        application,
        ICollection<string>? permissions,
        CancellationToken    ct = default
    ) {
        ct.ThrowIfCancellationRequested();

        application?.Permissions = permissions;

        return Task.CompletedTask;
    }

    public Task SetApplicationTypeAsync(TApplication? application, string? type, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        application?.ApplicationType = type;

        return Task.CompletedTask;
    }

    public Task SetSubjectTypeAsync(TApplication? application, string? type, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        application?.SubjectType = type;

        return Task.CompletedTask;
    }

    public Task SetSectorIdentifierUriAsync(TApplication? application, string? uri, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        application?.SectorIdentifierUri = uri;

        return Task.CompletedTask;
    }

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

    public async Task<TApplication?> CreateAsync(TApplication? application, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (application is null) {
            return null;
        }

        await _mutation.CreateAsync(application, null, ct);

        return application;
    }

    public async Task UpdateAsync(TApplication? application, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (application is null) {
            return;
        }

        await _mutation.UpdateAsync(application, null, Operations.Update, ct);
    }

    public async Task DeleteAsync(TApplication? application, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (application is null) {
            return;
        }

        await _mutation.DeleteAsync(application, null, Operations.Delete, ct);
    }

    public async Task EnlistPublicationAsync(IUnitOfWork transaction, string application, CancellationToken ct = default) {
        var applications = _services.GetRequiredService<IRepository<TApplication>>();
        applications.Join(transaction);
        using var owners = applications.SuppressQueryOwner();
        var current = await applications.SingleOrDefaultAsync(q => q.Where(a => a.CanonicalName == application), ct);
        if (current is null or ISoftDelete { DeleteTime: not null }) {
            throw SchemataResourceErrors.Aborted<TApplication>(application);
        }
        await _mutation.UpdateAsync(current, transaction, Operations.Update, ct);
    }

    public async Task EnlistTokenPublicationAsync(IUnitOfWork transaction, IReadOnlyCollection<SchemataToken> tokens, CancellationToken ct = default) {
        var applications = new HashSet<string>(StringComparer.Ordinal);
        var names = ResourceNameDescriptor.ForType<TApplication>();
        IRepository<TAuthorization>? authorizations = null;
        foreach (var token in tokens) {
            if (token.Application is { } application) applications.Add(application);
            if (token.Parent is { } parent && names.ParseCanonicalName(parent) is not null) applications.Add(parent);
            if (token.Authorization is not { } authorization) continue;
            if (authorizations is null) {
                authorizations = _services.GetRequiredService<IRepository<TAuthorization>>();
                authorizations.Join(transaction);
            }
            using var owners = authorizations.SuppressQueryOwner();
            var grant = await authorizations.SingleOrDefaultAsync(q => q.Where(a => a.CanonicalName == authorization), ct);
            if (grant is null or ISoftDelete { DeleteTime: not null }) {
                throw SchemataResourceErrors.Aborted<TAuthorization>(authorization);
            }
            if (grant.Application is { } grantedApplication) applications.Add(grantedApplication);
        }
        foreach (var application in applications) {
            await EnlistPublicationAsync(transaction, application, ct);
        }
    }


    public Task<bool> HasGrantTypeAsync(TApplication? application, string? grantType, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        return Task.FromResult(application is not null
                            && !string.IsNullOrWhiteSpace(grantType)
                            && SchemataApplicationMetadata.HasGrantType(application, grantType));
    }

    public Task<bool> HasResponseTypeAsync(TApplication? application, string? responseType, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        return Task.FromResult(application is not null
                            && !string.IsNullOrWhiteSpace(responseType)
                            && SchemataApplicationMetadata.HasResponseType(application, responseType));
    }

    public Task<bool> HasScopeAsync(TApplication? application, string? scope, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        return Task.FromResult(application is not null
                            && !string.IsNullOrWhiteSpace(scope)
                            && SchemataApplicationMetadata.HasScope(application, scope));
    }

    public Task PermitGrantTypeAsync(TApplication? application, string? grantType, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (application is null || string.IsNullOrWhiteSpace(grantType)) {
            return Task.CompletedTask;
        }

        application.GrantTypes ??= [];
        if (!application.GrantTypes.Contains(grantType)) {
            application.GrantTypes.Add(grantType);
        }

        return Task.CompletedTask;
    }

    public Task PermitEndpointAsync(TApplication? application, string? endpoint, CancellationToken ct = default) {
        return PermitAsync(application, PermissionPrefixes.Endpoint, endpoint, ct);
    }

    public Task PermitScopeAsync(TApplication? application, string? scope, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (application is null || string.IsNullOrWhiteSpace(scope)) {
            return Task.CompletedTask;
        }

        var registered = application.Scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList() ?? [];
        if (!registered.Contains(scope)) {
            registered.Add(scope);
            application.Scope = string.Join(' ', registered);
        }

        return Task.CompletedTask;
    }

    public Task RemovePermissionAsync(TApplication? application, string? permission, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (application is null || string.IsNullOrWhiteSpace(permission)) {
            return Task.CompletedTask;
        }

        application.Permissions?.Remove(permission);

        return Task.CompletedTask;
    }

    #endregion

    private static Task PermitAsync(
        TApplication?     application,
        string            prefix,
        string?           value,
        CancellationToken ct
    ) {
        ct.ThrowIfCancellationRequested();

        if (application is null || string.IsNullOrWhiteSpace(value)) {
            return Task.CompletedTask;
        }

        application.Permissions ??= [];

        var permission = prefix + value;
        if (!application.Permissions.Contains(permission)) {
            application.Permissions.Add(permission);
        }

        return Task.CompletedTask;
    }
}
