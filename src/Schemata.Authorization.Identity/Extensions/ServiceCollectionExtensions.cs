using System.Linq;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Authorization.Identity;
using Schemata.Authorization.Identity.Advisors;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Extension methods bridging ASP.NET Core Identity to the Authorization subject pipeline.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Registers <see cref="IdentitySubjectProvider{TUser}" /> closed over the user type Identity was
    ///     configured with, along with the subject-claims advisor. The user type is discovered from the
    ///     <see cref="IUserValidator{TUser}" /> registration Identity leaves behind, so calling this
    ///     before Identity is added registers nothing.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataIdentitySubjectProvider(this IServiceCollection services) {
        var descriptor = services.FirstOrDefault(d => d.ServiceType.IsGenericType
                                                   && d.ServiceType.GetGenericTypeDefinition() == typeof(IUserValidator<>));

        if (descriptor is null) {
            return services;
        }

        var user     = descriptor.ServiceType.GetGenericArguments()[0];
        var provider = typeof(IdentitySubjectProvider<>).MakeGenericType(user);

        services.TryAddScoped(typeof(ISubjectProvider), provider);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IClaimsAdvisor, AdviceClaimsSubject>());

        return services;
    }
}
