using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Entity.Repository;
using Schemata.Security.Skeleton.Entities;

namespace Schemata.Authorization.Foundation.Mutations;

/// <summary>
///     The <see cref="SchemataToken" /> resource mutation owner for management writes. Creation and
///     update stage the row through the actual supplied repository and then enlist the application
///     token publication fence in the same transaction; without a caller transaction the base
///     template self-commits that unit of work, so one actual transaction owner and the resource
///     committed callbacks serve every management path. Delete stages through the default template
///     without a publication fence. Protocol atomic token operations (redemption, family
///     revocation, slot establishment) keep their separate store contracts.
/// </summary>
/// <typeparam name="TApp">The configured application entity type.</typeparam>
internal sealed class TokenResourceMutation<TApp>(
    IApplicationManager<TApp> applications,
    IServiceProvider          services
) : ResourceMutation<SchemataToken>(services)
    where TApp : SchemataApplication
{
    /// <summary>
    ///     Stages the row, then enlists the publication fence in the same transaction.
    /// </summary>
    protected override async Task<MutationResult> StageCreateAsync(
        IRepository<SchemataToken> repository,
        SchemataToken              entity,
        IUnitOfWork                transaction,
        CancellationToken          ct
    ) {
        var staged = await base.StageCreateAsync(repository, entity, transaction, ct);
        if (staged == MutationResult.Applied) {
            await applications.EnlistTokenPublicationAsync(transaction, [entity], ct);
        }

        return staged;
    }

    /// <summary>
    ///     Stages the row, then enlists the publication fence in the same transaction.
    /// </summary>
    protected override async Task<MutationResult> StageUpdateAsync(
        IRepository<SchemataToken> repository,
        SchemataToken              entity,
        IUnitOfWork                transaction,
        Operations                 operation,
        CancellationToken          ct
    ) {
        var staged = await base.StageUpdateAsync(repository, entity, transaction, operation, ct);
        if (staged == MutationResult.Applied) {
            await applications.EnlistTokenPublicationAsync(transaction, [entity], ct);
        }

        return staged;
    }
}
