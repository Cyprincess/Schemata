using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Common.Errors;
using Schemata.Entity.Repository;
using Schemata.Identity.Skeleton.Entities;

namespace Schemata.Identity.Skeleton.Mutations;

/// <summary>
///     The role resource mutation owner shared by the Identity stores and the resource entry.
///     Delete staging removes the role's user links and claims in the same unit of work once the
///     role removal applied, so a blocked removal leaves every dependent untouched and a failing
///     cleanup rolls the role removal back with it.
/// </summary>
/// <typeparam name="TRole">The role entity type.</typeparam>
/// <typeparam name="TRoleClaim">The role claim entity type.</typeparam>
/// <typeparam name="TUserRole">The user-role link entity type.</typeparam>
public class RoleResourceMutation<TRole, TRoleClaim, TUserRole> : ResourceMutation<TRole>
    where TRole : SchemataRole
    where TRoleClaim : SchemataRoleClaim
    where TUserRole : SchemataUserRole
{
    /// <summary>
    ///     Initializes the owner over the scoped service provider used to resolve relation
    ///     repositories during delete staging.
    /// </summary>
    /// <param name="services">The scoped service provider.</param>
    public RoleResourceMutation(IServiceProvider services) : base(services) { }

    /// <summary>
    ///     Stages the role removal first, then the dependent-row cleanup when the removal applied.
    ///     Relation rows are protocol storage without their own mutation owner, so they stage
    ///     through their repositories inside this transaction; a dependent removal that an advisor
    ///     blocks vetoes the whole deletion.
    /// </summary>
    protected override async Task<MutationResult> StageDeleteAsync(
        IRepository<TRole> repository,
        TRole              entity,
        IUnitOfWork        transaction,
        Operations         operation,
        CancellationToken  ct
    ) {
        var result = await base.StageDeleteAsync(repository, entity, transaction, operation, ct);
        if (result != MutationResult.Applied) {
            return result;
        }

        var userRole   = Services.GetRequiredService<IRepository<TUserRole>>();
        var roleClaims = Services.GetRequiredService<IRepository<TRoleClaim>>();
        userRole.Join(transaction);
        roleClaims.Join(transaction);

        await foreach (var link in userRole.ListAsync(q => q.Where(ur => ur.RoleId == entity.CanonicalName), ct)) {
            if (await userRole.RemoveAsync(link, ct) != MutationResult.Applied) {
                throw Vetoed<TUserRole>(entity);
            }
        }

        await foreach (var claim in roleClaims.ListAsync(q => q.Where(rc => rc.RoleId == entity.CanonicalName), ct)) {
            if (await roleClaims.RemoveAsync(claim, ct) != MutationResult.Applied) {
                throw Vetoed<TRoleClaim>(entity);
            }
        }

        return result;
    }

    private static FailedPreconditionException Vetoed<TDependent>(TRole entity) {
        return SchemataResourceErrors.PreconditionFailed<TDependent>(
            entity.CanonicalName,
            description: "A blocked dependent removal vetoes the deletion.");
    }
}
