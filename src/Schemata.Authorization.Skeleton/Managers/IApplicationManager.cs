using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Entities;

namespace Schemata.Authorization.Skeleton.Managers;

/// <summary>
///     Manages <see cref="SchemataApplication" /> entities.
///     Handles CRUD and property validation for OAuth 2.0 clients.
/// </summary>
public interface IApplicationManager<TApplication>
    where TApplication : SchemataApplication
{
    /// <summary>Lists applications matching the optional predicate.</summary>
    IAsyncEnumerable<TApplication> ListAsync(
        Func<IQueryable<TApplication>, IQueryable<TApplication>>? predicate,
        CancellationToken                                         ct = default
    );

    /// <summary>Finds an application by its client_id.</summary>
    Task<TApplication?> FindByClientIdAsync(string? clientId, CancellationToken ct = default);

    /// <summary>
    ///     Validates a client secret against the stored hash.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-2.3.1">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §2.3.1: Client Password
    ///     </seealso>
    /// </summary>
    Task<bool> ValidateClientSecretAsync(TApplication? application, string? secret, CancellationToken ct = default);

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

    /// <summary>Stores a hashed client secret for the application.</summary>
    Task SetClientSecretAsync(TApplication? application, string? secret, CancellationToken ct = default);

    /// <summary>Sets the display name for the application.</summary>
    Task SetDisplayNameAsync(TApplication? application, string? name, CancellationToken ct = default);

    /// <summary>Sets localized display names for the application.</summary>
    Task SetDisplayNamesAsync(
        TApplication?               application,
        Dictionary<string, string>? names,
        CancellationToken           ct = default
    );

    /// <summary>Sets the description for the application.</summary>
    Task SetDescriptionAsync(TApplication? application, string? description, CancellationToken ct = default);

    /// <summary>Sets localized descriptions for the application.</summary>
    Task SetDescriptionsAsync(
        TApplication?               application,
        Dictionary<string, string>? descriptions,
        CancellationToken           ct = default
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

    /// <summary>
    ///     Sets the OAuth 2.0 client type.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-2.1">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §2.1: Client Types
    ///     </seealso>
    /// </summary>
    Task SetClientTypeAsync(TApplication? application, string? type, CancellationToken ct = default);

    /// <summary>Sets the application type.</summary>
    Task SetApplicationTypeAsync(TApplication? application, string? type, CancellationToken ct = default);

    /// <summary>Sets the consent type.</summary>
    Task SetConsentTypeAsync(TApplication? application, string? type, CancellationToken ct = default);

    /// <summary>Sets whether PKCE is required for this application.</summary>
    Task SetRequirePkceAsync(TApplication? application, bool? require, CancellationToken ct = default);

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

    /// <summary>Creates a new application.</summary>
    Task<TApplication?> CreateAsync(TApplication? application, CancellationToken ct = default);

    /// <summary>Updates an existing application.</summary>
    Task UpdateAsync(TApplication? application, CancellationToken ct = default);

    /// <summary>Deletes an application.</summary>
    Task DeleteAsync(TApplication? application, CancellationToken ct = default);
}
