using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Entities;

namespace Schemata.Authorization.Skeleton.Managers;

public interface IApplicationManager<TApplication>
    where TApplication : SchemataApplication
{
    IAsyncEnumerable<TApplication> ListAsync(
        Func<IQueryable<TApplication>, IQueryable<TApplication>>? predicate,
        CancellationToken                                         ct = default
    );

    Task<TApplication?> FindByCanonicalNameAsync(string? name, CancellationToken ct = default);

    Task<bool> ValidateClientSecretAsync(TApplication? application, string? secret, CancellationToken ct = default);

    Task<bool> ValidateRedirectUriAsync(TApplication? application, string? uri, CancellationToken ct = default);

    Task<bool> ValidatePostLogoutRedirectUriAsync(
        TApplication?     application,
        string?           uri,
        CancellationToken ct = default
    );

    Task<bool> HasPermissionAsync(TApplication? application, string? permission, CancellationToken ct = default);

    Task SetClientSecretAsync(TApplication? application, string? secret, CancellationToken ct = default);

    Task SetDisplayNameAsync(TApplication? application, string? name, CancellationToken ct = default);

    Task SetDisplayNamesAsync(
        TApplication?               application,
        Dictionary<string, string>? names,
        CancellationToken           ct = default
    );

    Task SetDescriptionAsync(TApplication? application, string? description, CancellationToken ct = default);

    Task SetDescriptionsAsync(
        TApplication?               application,
        Dictionary<string, string>? descriptions,
        CancellationToken           ct = default
    );

    Task SetRedirectUrisAsync(TApplication? application, ICollection<string> uris, CancellationToken ct = default);

    Task SetPostLogoutRedirectUrisAsync(
        TApplication?        application,
        ICollection<string>? uris,
        CancellationToken    ct = default
    );

    Task SetPermissionsAsync(
        TApplication?        application,
        ICollection<string>? permissions,
        CancellationToken    ct = default
    );

    Task SetClientTypeAsync(TApplication? application, string? type, CancellationToken ct = default);

    Task SetApplicationTypeAsync(TApplication? application, string? type, CancellationToken ct = default);

    Task SetConsentTypeAsync(TApplication? application, string? type, CancellationToken ct = default);

    Task SetRequirePkceAsync(TApplication? application, bool? require, CancellationToken ct = default);

    Task SetSubjectTypeAsync(TApplication? application, string? type, CancellationToken ct = default);

    Task SetSectorIdentifierUriAsync(TApplication? application, string? uri, CancellationToken ct = default);

    Task SetFrontChannelLogoutAsync(
        TApplication?     application,
        string?           uri,
        bool              session,
        CancellationToken ct = default
    );

    Task SetBackChannelLogoutAsync(
        TApplication?     application,
        string?           uri,
        bool              session,
        CancellationToken ct = default
    );

    Task<TApplication?> CreateAsync(TApplication? application, CancellationToken ct = default);

    Task UpdateAsync(TApplication? application, CancellationToken ct = default);

    Task DeleteAsync(TApplication? application, CancellationToken ct = default);
}
