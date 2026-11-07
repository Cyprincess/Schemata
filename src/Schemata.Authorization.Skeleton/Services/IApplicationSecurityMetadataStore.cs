using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Entity.Repository;

namespace Schemata.Authorization.Skeleton.Services;

/// <summary>Stages and projects public client key metadata within an application provisioning transaction.</summary>
public interface IApplicationSecurityMetadataStore
{
    Task ProjectAsync(SchemataApplication application, RegistrationResponse response, CancellationToken ct);

    Task ReplaceAsync(
        SchemataApplication application,
        RegisterRequest request,
        IUnitOfWork transaction,
        CancellationToken ct
    );
}
