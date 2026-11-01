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
///     The user resource mutation owner shared by the Identity stores and the resource entry.
///     Delete staging removes the user's role links, claims, logins, and tokens in the same unit of
///     work once the user removal applied, so a blocked removal leaves every dependent untouched
///     and a failing cleanup rolls the user removal back with it.
/// </summary>
/// <typeparam name="TUser">The user entity type.</typeparam>
/// <typeparam name="TUserClaim">The user claim entity type.</typeparam>
/// <typeparam name="TUserRole">The user-role link entity type.</typeparam>
/// <typeparam name="TUserLogin">The user login entity type.</typeparam>
/// <typeparam name="TUserToken">The user token entity type.</typeparam>
public class UserResourceMutation<TUser, TUserClaim, TUserRole, TUserLogin, TUserToken> : ResourceMutation<TUser>
    where TUser : SchemataUser
    where TUserClaim : SchemataUserClaim
    where TUserRole : SchemataUserRole
    where TUserLogin : SchemataUserLogin
    where TUserToken : SchemataUserToken
{
    /// <summary>
    ///     Initializes the owner over the scoped service provider used to resolve relation
    ///     repositories during delete staging.
    /// </summary>
    /// <param name="services">The scoped service provider.</param>
    public UserResourceMutation(IServiceProvider services) : base(services) { }

    /// <summary>
    ///     Stages the user removal first, then the dependent-row cleanup when the removal applied.
    ///     Relation rows are protocol storage without their own mutation owner, so they stage
    ///     through their repositories inside this transaction; a dependent removal that an advisor
    ///     blocks vetoes the whole deletion.
    /// </summary>
    protected override async Task<MutationResult> StageDeleteAsync(
        IRepository<TUser> repository,
        TUser              entity,
        IUnitOfWork        transaction,
        Operations         operation,
        CancellationToken  ct
    ) {
        var result = await base.StageDeleteAsync(repository, entity, transaction, operation, ct);
        if (result != MutationResult.Applied) {
            return result;
        }

        var userRole   = Services.GetRequiredService<IRepository<TUserRole>>();
        var userClaims = Services.GetRequiredService<IRepository<TUserClaim>>();
        var userLogins = Services.GetRequiredService<IRepository<TUserLogin>>();
        var userTokens = Services.GetRequiredService<IRepository<TUserToken>>();
        userRole.Join(transaction);
        userClaims.Join(transaction);
        userLogins.Join(transaction);
        userTokens.Join(transaction);

        await foreach (var link in userRole.ListAsync(q => q.Where(ur => ur.UserId == entity.CanonicalName), ct)) {
            if (await userRole.RemoveAsync(link, ct) != MutationResult.Applied) {
                throw Vetoed<TUserRole>(entity);
            }
        }

        await foreach (var claim in userClaims.ListAsync(q => q.Where(uc => uc.UserId == entity.CanonicalName), ct)) {
            if (await userClaims.RemoveAsync(claim, ct) != MutationResult.Applied) {
                throw Vetoed<TUserClaim>(entity);
            }
        }

        await foreach (var login in userLogins.ListAsync(q => q.Where(l => l.UserId == entity.CanonicalName), ct)) {
            if (await userLogins.RemoveAsync(login, ct) != MutationResult.Applied) {
                throw Vetoed<TUserLogin>(entity);
            }
        }

        await foreach (var token in userTokens.ListAsync(q => q.Where(t => t.UserId == entity.CanonicalName), ct)) {
            if (await userTokens.RemoveAsync(token, ct) != MutationResult.Applied) {
                throw Vetoed<TUserToken>(entity);
            }
        }

        return result;
    }

    private static FailedPreconditionException Vetoed<TDependent>(TUser entity) {
        return SchemataResourceErrors.PreconditionFailed<TDependent>(
            entity.CanonicalName,
            description: "A blocked dependent removal vetoes the deletion.");
    }
}
