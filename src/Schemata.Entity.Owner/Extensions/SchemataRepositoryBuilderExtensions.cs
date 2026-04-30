using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Entity.Owner;
using Schemata.Entity.Owner.Advisors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods for <see cref="SchemataRepositoryBuilder" /> to enable query caching
///     together with immediate eviction on update and remove.
/// </summary>
public static class SchemataRepositoryBuilderExtensions
{
    /// <summary>
    ///     Registers the ownirsnop.
    /// </summary>
    /// <param name="builder">The repository builder.</param>
    /// <returns>The same builder for chaining.</returns>
    public static SchemataRepositoryBuilder UseOwner(
        this SchemataRepositoryBuilder     builder
    ) {
        builder.Services.TryAdd(ServiceDescriptor.Scoped(typeof(IOwnerResolver<>), typeof(NullOwnerResolver<>)));

        builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryBuildQueryAdvisor<>), typeof(AdviceBuildQueryOwner<>)));
        builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryAddAdvisor<>), typeof(AdviceAddOwner<>)));
        
        return builder;
    }
}
