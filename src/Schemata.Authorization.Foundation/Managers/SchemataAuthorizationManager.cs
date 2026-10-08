using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Entity.Repository;
using Schemata.Security.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Managers;

/// <summary>
///     Default implementation of <see cref="IAuthorizationManager{TAuthorization}" /> backed by an
///     <see cref="IRepository{TEntity}" /> for reads and the injected
///     <see cref="IResourceMutation{TAuthorization}" /> owner for writes.
/// </summary>
/// <typeparam name="TAuthorization">The authorization entity type, must derive from <see cref="SchemataAuthorization" />.</typeparam>
/// <typeparam name="TApplication">The configured application entity type.</typeparam>
/// <remarks>
///     Authorizations represent a user's consent to a specific application for a set of scopes. They are
///     keyed by canonical subject + canonical application name. Revocation sets the status to
///     <see cref="TokenStatuses.Revoked" /> without physically deleting the record. Creation and update
///     run in one caller-owned transaction with the application publication fence.
/// </remarks>
public class SchemataAuthorizationManager<TAuthorization, TApplication> : IAuthorizationManager<TAuthorization>
    where TAuthorization : SchemataAuthorization
    where TApplication : SchemataApplication
{
    private readonly IApplicationManager<TApplication>  _applications;
    private readonly IResourceMutation<TAuthorization>  _mutation;
    private readonly IServiceProvider                   _services;

    public SchemataAuthorizationManager(
        IApplicationManager<TApplication> applications,
        IResourceMutation<TAuthorization> mutation,
        IServiceProvider                  services
    ) {
        _applications   = applications;
        _mutation       = mutation;
        _services       = services;
    }

    #region IAuthorizationManager<TAuthorization> Members

    /// <summary>
    ///     Streams authorizations matching <paramref name="subject" /> and
    ///     <paramref name="application" />. Both arguments are AIP-122 canonical names
    ///     (<c>users/{uid}</c> and <c>applications/{client}</c>) since
    ///     <see cref="SchemataAuthorization.Subject" /> and
    ///     <see cref="SchemataAuthorization.Application" /> are persisted in canonical form.
    /// </summary>
    public async IAsyncEnumerable<TAuthorization> ListAsync(
        string?                                    subject,
        string?                                    application,
        [EnumeratorCancellation] CancellationToken ct = default
    ) {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(subject)) {
            yield break;
        }

        if (string.IsNullOrWhiteSpace(application)) {
            yield break;
        }

        await using var authorizations = _services.GetRequiredService<IRepository<TAuthorization>>();
        await foreach (var authorization in authorizations.ListAsync(
                           q => q.Where(a => a.Subject == subject && a.Application == application), ct)) {
            yield return authorization;
        }
    }

    public async Task<TAuthorization?> CreateAsync(TAuthorization? authorization, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (authorization is null) {
            return null;
        }

        var authorizations = _services.GetRequiredService<IRepository<TAuthorization>>();
        await using var transaction = authorizations.Begin();
        await _mutation.CreateAsync(authorization, transaction, ct);
        await _applications.EnlistPublicationAsync(transaction, authorization.Application!, ct);
        await transaction.CommitAsync(ct);

        return authorization;
    }

    public async Task RevokeAsync(TAuthorization? authorization, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (authorization is null) {
            return;
        }

        authorization.Status = TokenStatuses.Revoked;

        await _mutation.UpdateAsync(authorization, null, Operations.Update, ct);
    }

    public async Task UpdateAsync(TAuthorization? authorization, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (authorization is null) {
            return;
        }

        var authorizations = _services.GetRequiredService<IRepository<TAuthorization>>();
        await using var transaction = authorizations.Begin();
        await _mutation.UpdateAsync(authorization, transaction, Operations.Update, ct);
        await _applications.EnlistPublicationAsync(transaction, authorization.Application!, ct);
        await transaction.CommitAsync(ct);
    }

    public async Task DeleteAsync(TAuthorization? authorization, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (authorization is null) {
            return;
        }

        await _mutation.DeleteAsync(authorization, null, Operations.Delete, ct);
    }

    #endregion
}
