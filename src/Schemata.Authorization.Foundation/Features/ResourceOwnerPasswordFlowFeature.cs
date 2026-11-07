using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Core;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>Registers the explicitly enabled historical password grant and its discovery metadata.</summary>
public sealed class ResourceOwnerPasswordFlowFeature<TApp> : IAuthorizationFlowFeature
    where TApp : SchemataApplication
{
    /// <inheritdoc />
    public int Order => ResourceOwnerPasswordFlowFeature.DefaultOrder;

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        services.TryAddKeyedScoped<IGrantHandler, ResourceOwnerPasswordHandler<TApp>>(GrantTypes.Password);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryResourceOwnerPassword>());
    }

    private sealed class AdviceDiscoveryResourceOwnerPassword : IDiscoveryAdvisor
    {
        public int Order => AdviceDiscoveryClientCredentials.DefaultOrder + 10_000_000;

        public Task<AdviseResult> AdviseAsync(AdviceContext ctx, DiscoveryContext discovery, CancellationToken ct = default) {
            discovery.Document ??= new();
            discovery.Document.GrantTypesSupported ??= [];
            discovery.Document.GrantTypesSupported.Add(GrantTypes.Password);
            return Task.FromResult(AdviseResult.Continue);
        }
    }
}

internal static class ResourceOwnerPasswordFlowFeature
{
    public const int DefaultOrder = DeviceFlowFeature.DefaultOrder + 100;
}
