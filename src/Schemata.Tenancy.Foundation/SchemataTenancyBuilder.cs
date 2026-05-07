using Microsoft.Extensions.DependencyInjection;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation;

/// <summary>
///     Fluent builder for configuring multi-tenancy resolvers and per-tenant services.
/// </summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public sealed class SchemataTenancyBuilder<TTenant>
    where TTenant : SchemataTenant
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SchemataTenancyBuilder{TTenant}" /> class.
    /// </summary>
    public SchemataTenancyBuilder(IServiceCollection services) { Services = services; }

    /// <summary>Gets the service collection for registering tenant-related services.</summary>
    public IServiceCollection Services { get; }
}
