using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Flow.Foundation.Features;
using Schemata.Flow.Http.Linking;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Resource.Http.Features;

namespace Schemata.Flow.Http.Features;

/// <summary>Registers Flow resources for the HTTP resource transport.</summary>
/// <typeparam name="TProcess">The persisted process entity type registered by the host.</typeparam>
/// <typeparam name="TToken">The persisted token entity type registered by the host.</typeparam>
/// <typeparam name="TTransition">The persisted transition entity type registered by the host.</typeparam>
[DependsOn<SchemataFlowFeature>]
[DependsOn<SchemataHttpResourceFeature>]
public sealed class SchemataFlowHttpFeature<TProcess, TToken, TTransition> : FeatureBase
    where TProcess : SchemataProcess
    where TToken : SchemataProcessToken
    where TTransition : SchemataProcessTransition
{
    /// <summary>Default <see cref="FeatureBase.Priority" /> for the Flow HTTP feature.</summary>
    public const int DefaultPriority = SchemataFlowFeature.DefaultPriority + 100_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.AddSchemataApplicationPart<SchemataFlowHttpFeature<TProcess, TToken, TTransition>>();
        services.AddSchemataFlowHttpResources<TProcess, TToken, TTransition>(schemata);
        services.AddSchemataLinkContributor<FlowLinkContributor>();
    }
}
