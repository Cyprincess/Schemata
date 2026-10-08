using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Tenancy;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Skeleton;

public interface ITenantServiceScopeFactory<TTenant> where TTenant : SchemataTenant
{
    ValueTask<AsyncServiceScope> CreateAsync(TenantIdentity identity, CancellationToken ct = default);
}
