using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods for <see cref="SchemataRepositoryBuilder" /> to enable the
///     ownership advisor pipeline.
/// </summary>
public static class SchemataRepositoryBuilderOwnerExtensions
{
    /// <summary>
    ///     Registers ownership query and mutation policy. Hosts should register a real
    ///     <see cref="IOwnerResolver{TEntity}" /> or configure
    ///     <see cref="SchemataOwnerOptions.OnNullOwner" /> away from
    ///     <see cref="OnNullOwnerPolicy.Reject" />. Reference format and existence validation stay
    ///     installed by the repository defaults, independent of this opt-in.
    /// </summary>
    /// <param name="builder">The repository builder.</param>
    /// <returns>The same builder for chaining.</returns>
    public static SchemataRepositoryBuilder UseOwner(this SchemataRepositoryBuilder builder) {
        builder.Services.AddOptions<SchemataOwnerOptions>();

        builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryBuildQueryAdvisor<>), typeof(AdviceBuildQueryOwner<>)));
        builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryAddAdvisor<>), typeof(AdviceAddOwner<>)));

        return builder;
    }
}
