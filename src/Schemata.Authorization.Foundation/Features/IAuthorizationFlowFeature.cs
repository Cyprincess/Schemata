using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;

namespace Schemata.Authorization.Foundation.Features;

public interface IAuthorizationFlowFeature
{
    int Order { get; }

    void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators);
}
