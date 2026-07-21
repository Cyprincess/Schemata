using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Authorization.Identity.Advisors;
using Schemata.Authorization.Foundation.Features;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Identity.Foundation.Features;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Identity.Features;

/// <summary>
///     Wires Schemata's Identity-backed <see cref="ISubjectProvider" /> and the subject-claims advisor into the
///     Authorization pipeline.
/// </summary>
[DependsOn(typeof(SchemataAuthorizationFeature<,,,>))]
[DependsOn(typeof(SchemataIdentityFeature<,,,>))]
public sealed class SchemataAuthorizationIdentityFeature : FeatureBase
{
    /// <summary>Default feature priority for Identity-backed authorization integration.</summary>
    public const int DefaultPriority = Orders.Extension + 50_100_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        var descriptor = services.FirstOrDefault(d => d.ServiceType.IsGenericType && d.ServiceType.GetGenericTypeDefinition() == typeof(IUserValidator<>));

        if (descriptor is null) {
            return;
        }

        var user     = descriptor.ServiceType.GetGenericArguments()[0];
        var provider = typeof(IdentitySubjectProvider<>).MakeGenericType(user);

        services.TryAddScoped(typeof(ISubjectProvider), provider);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IClaimsAdvisor, AdviceClaimsSubject>());
    }
}
