using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository;
using Schemata.Security.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Entities;

namespace Schemata.Authorization.Skeleton.Managers;

/// <summary>
///     Manages <see cref="SchemataApplication" /> entities.
///     Handles CRUD and property validation for OAuth 2.0 clients. Writes adapt the injected
///     <see cref="IResourceMutation{TApplication}" /> owner; this interface carries no parallel
///     mutation contract.
/// </summary>
public interface IApplicationManager<TApplication>
    where TApplication : SchemataApplication
{
    /// <summary>Creates an application.</summary>
    Task<TApplication?> CreateAsync(TApplication? application, CancellationToken ct = default);

    /// <summary>Updates an application.</summary>
    Task UpdateAsync(TApplication? application, CancellationToken ct = default);

    /// <summary>Deletes an application.</summary>
    Task DeleteAsync(TApplication? application, CancellationToken ct = default);

    /// <summary>Lists applications matching the optional predicate.</summary>
    IAsyncEnumerable<TApplication> ListAsync(
        Func<IQueryable<TApplication>, IQueryable<TApplication>>? predicate,
        CancellationToken                                         ct = default
    );

    /// <summary>Finds an application by its client_id.</summary>
    Task<TApplication?> FindByClientIdAsync(string? clientId, CancellationToken ct = default);

    /// <summary>
    ///     Validates that a redirect URI is registered for the application.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-3.1.2">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §3.1.2: Redirection Endpoint
    ///     </seealso>
    /// </summary>
    Task<bool> ValidateRedirectUriAsync(TApplication? application, string? uri, CancellationToken ct = default);

    /// <summary>Validates that a post-logout redirect URI is registered for the application.</summary>
    Task<bool> ValidatePostLogoutRedirectUriAsync(
        TApplication?     application,
        string?           uri,
        CancellationToken ct = default
    );

    /// <summary>Checks whether the application has a specific permission.</summary>
    Task<bool> HasPermissionAsync(TApplication? application, string? permission, CancellationToken ct = default);

    /// <summary>Sets the <c>client_name</c> for the application.</summary>
    Task SetClientNameAsync(TApplication? application, string? name, CancellationToken ct = default);

    /// <summary>Sets localized display names for the application.</summary>
    Task SetDisplayNamesAsync(
        TApplication?                application,
        Dictionary<string, string?>? names,
        CancellationToken            ct = default
    );

    /// <summary>Sets the description for the application.</summary>
    Task SetDescriptionAsync(TApplication? application, string? description, CancellationToken ct = default);

    /// <summary>Sets localized descriptions for the application.</summary>
    Task SetDescriptionsAsync(
        TApplication?                application,
        Dictionary<string, string?>? descriptions,
        CancellationToken            ct = default
    );

    /// <summary>Sets the registered redirect URIs for the application.</summary>
    Task SetRedirectUrisAsync(TApplication? application, ICollection<string> uris, CancellationToken ct = default);

    /// <summary>Sets the registered post-logout redirect URIs for the application.</summary>
    Task SetPostLogoutRedirectUrisAsync(
        TApplication?        application,
        ICollection<string>? uris,
        CancellationToken    ct = default
    );

    /// <summary>Sets the granted permissions for the application.</summary>
    Task SetPermissionsAsync(
        TApplication?        application,
        ICollection<string>? permissions,
        CancellationToken    ct = default
    );

    /// <summary>Permits a grant type for the application.</summary>
    /// <summary>Whether the client's registered <c>grant_types</c> include <paramref name="grantType" />.</summary>
    Task<bool> HasGrantTypeAsync(TApplication? application, string? grantType, CancellationToken ct = default);

    /// <summary>Whether the client's registered <c>response_types</c> include <paramref name="responseType" /> (token order insignificant).</summary>
    Task<bool> HasResponseTypeAsync(TApplication? application, string? responseType, CancellationToken ct = default);

    /// <summary>Whether the client's registered <c>scope</c> includes <paramref name="scope" />.</summary>
    Task<bool> HasScopeAsync(TApplication? application, string? scope, CancellationToken ct = default);

    Task PermitGrantTypeAsync(TApplication? application, string? grantType, CancellationToken ct = default);

    /// <summary>Permits an endpoint for the application.</summary>
    Task PermitEndpointAsync(TApplication? application, string? endpoint, CancellationToken ct = default);

    /// <summary>Permits a scope for the application.</summary>
    Task PermitScopeAsync(TApplication? application, string? scope, CancellationToken ct = default);

    /// <summary>Removes a permission from the application.</summary>
    Task RemovePermissionAsync(TApplication? application, string? permission, CancellationToken ct = default);

    /// <summary>Sets the application type.</summary>
    Task SetApplicationTypeAsync(TApplication? application, string? type, CancellationToken ct = default);

    /// <summary>Sets the subject identifier type.</summary>
    Task SetSubjectTypeAsync(TApplication? application, string? type, CancellationToken ct = default);

    /// <summary>Sets the sector identifier URI for pairwise subject identifiers.</summary>
    Task SetSectorIdentifierUriAsync(TApplication? application, string? uri, CancellationToken ct = default);

    /// <summary>Configures front-channel logout for the application.</summary>
    Task SetFrontChannelLogoutAsync(
        TApplication?     application,
        string?           uri,
        bool              session,
        CancellationToken ct = default
    );

    /// <summary>Configures back-channel logout for the application.</summary>
    Task SetBackChannelLogoutAsync(
        TApplication?     application,
        string?           uri,
        bool              session,
        CancellationToken ct = default
    );

    /// <summary>Joins a current application concurrency check to the caller-owned publication transaction.</summary>
    Task EnlistPublicationAsync(IUnitOfWork transaction, string application, CancellationToken ct = default);

    /// <summary>Joins the canonical application owners of a token batch to its publication transaction.</summary>
    Task EnlistTokenPublicationAsync(IUnitOfWork transaction, IReadOnlyCollection<SchemataToken> tokens, CancellationToken ct = default);
}
