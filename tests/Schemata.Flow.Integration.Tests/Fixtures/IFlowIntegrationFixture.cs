using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Flow.Integration.Tests.Fixtures;

public interface IFlowIntegrationFixture
{
    IServiceScope CreateScope();
}