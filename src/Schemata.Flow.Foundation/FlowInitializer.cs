using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Foundation;

internal sealed class FlowInitializer : BackgroundService
{
    private readonly IProcessRegistry _registry;
    private readonly IOptions<SchemataFlowOptions> _options;

    public FlowInitializer(IProcessRegistry registry, IOptions<SchemataFlowOptions> options)
    {
        _registry = registry;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken st)
    {
        foreach (var config in _options.Value.Configurations)
        {
            await _registry.RegisterAsync(config, st);
        }
    }
}
