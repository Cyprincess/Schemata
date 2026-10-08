using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Entity.Repository;
using Schemata.Security.Skeleton.Entities;

namespace Schemata.Authorization.Foundation.Mutations;

/// <summary>
///     The application resource mutation owner shared by the application manager and the resource
///     entry. Deleting an application — or persisting one whose <see cref="ISoftDelete.DeleteTime" />
///     is set — physically removes its canonical-reference security materials, authorization grants,
///     pairwise mappings, and tokens (including session participants and inactive rows) inside the
///     same unit of work. Shared issuer keys, users, OP sessions, refresh-family markers, and other
///     applications survive.
/// </summary>
/// <typeparam name="TApplication">The application entity type.</typeparam>
/// <typeparam name="TAuthorization">The configured consent entity type included in cleanup.</typeparam>
public class ApplicationResourceMutation<TApplication, TAuthorization> : ResourceMutation<TApplication>
    where TApplication : SchemataApplication
    where TAuthorization : SchemataAuthorization
{
    /// <summary>
    ///     Initializes the owner over the scoped service provider used to resolve dependent
    ///     repositories and resource mutations during cleanup.
    /// </summary>
    /// <param name="services">The scoped service provider.</param>
    public ApplicationResourceMutation(IServiceProvider services) : base(services) { }

    /// <summary>
    ///     Stages the update first; when it applied and persists a soft deletion, the dependent
    ///     cleanup stages in the same unit of work. A blocked or no-write primary leaves every
    ///     dependent untouched, and a failing cleanup rolls the primary back with it.
    /// </summary>
    protected override async Task<MutationResult> StageUpdateAsync(
        IRepository<TApplication> repository,
        TApplication              entity,
        IUnitOfWork               transaction,
        Operations                operation,
        CancellationToken         ct
    ) {
        var result = await base.StageUpdateAsync(repository, entity, transaction, operation, ct);
        if (result == MutationResult.Applied && entity is ISoftDelete { DeleteTime: not null }) {
            await DeleteDependentsAsync(entity, transaction, ct);
        }

        return result;
    }

    /// <summary>
    ///     Stages the application removal first, then the dependent cleanup when the removal
    ///     applied. Grant rows delete physically (<see cref="Operations.Expunge" />) so the cleanup
    ///     matches the manager's soft-delete-suppressed removal. A blocked removal leaves every
    ///     dependent untouched, and a failing cleanup rolls the removal back with it.
    /// </summary>
    protected override async Task<MutationResult> StageDeleteAsync(
        IRepository<TApplication> repository,
        TApplication              entity,
        IUnitOfWork               transaction,
        Operations                operation,
        CancellationToken         ct
    ) {
        var result = await base.StageDeleteAsync(repository, entity, transaction, operation, ct);
        if (result == MutationResult.Applied) {
            await DeleteDependentsAsync(entity, transaction, ct);
        }

        return result;
    }

    private async Task DeleteDependentsAsync(TApplication application, IUnitOfWork transaction, CancellationToken ct) {
        var name = application.CanonicalName;
        if (string.IsNullOrWhiteSpace(name)) {
            throw new InvalidOperationException("Application cleanup requires its canonical name.");
        }

        var authorizations = Services.GetRequiredService<IRepository<TAuthorization>>();
        authorizations.Join(transaction);

        List<TAuthorization> grants;
        using (authorizations.SuppressQuerySoftDelete())
        using (authorizations.SuppressQueryOwner()) {
            grants = await authorizations.ListAsync(q => q.Where(a => a.Application == name), ct).ToListAsync(ct);
        }

        var grantNames = grants.Select(a => a.CanonicalName).Where(n => n != null).ToArray();

        var tokens = Services.GetRequiredService<IRepository<SchemataToken>>();
        tokens.Join(transaction);
        var credentials = await tokens.ListAsync(
                                      q => q.Where(t => t.Application == name
                                                     || t.Parent == name
                                                     || grantNames.Contains(t.Authorization)),
                                      ct)
                                  .ToListAsync(ct);

        var securities = Services.GetRequiredService<IRepository<SchemataSecurity>>();
        securities.Join(transaction);
        var materials = await securities.ListAsync(q => q.Where(s => s.Parent == name), ct).ToListAsync(ct);

        var mappings = Services.GetService<IRepository<SchemataSubjectMapping>>();
        List<SchemataSubjectMapping>? pairs = null;
        if (mappings is not null) {
            mappings.Join(transaction);
            pairs = await mappings.ListAsync(q => q.Where(m => m.Application == name), ct).ToListAsync(ct);
        }

        var tokenMutation = Services.GetRequiredService<IResourceMutation<SchemataToken>>();
        foreach (var credential in credentials) {
            await tokenMutation.DeleteAsync(credential, transaction, Operations.Delete, ct);
        }

        var securityMutation = Services.GetRequiredService<IResourceMutation<SchemataSecurity>>();
        foreach (var material in materials) {
            await securityMutation.DeleteAsync(material, transaction, Operations.Delete, ct);
        }

        var authorizationMutation = Services.GetRequiredService<IResourceMutation<TAuthorization>>();
        foreach (var grant in grants) {
            await authorizationMutation.DeleteAsync(grant, transaction, Operations.Expunge, ct);
        }

        if (pairs is not null) {
            var mappingMutation = Services.GetRequiredService<IResourceMutation<SchemataSubjectMapping>>();
            foreach (var pair in pairs) {
                await mappingMutation.DeleteAsync(pair, transaction, Operations.Delete, ct);
            }
        }
    }
}
