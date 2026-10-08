using Microsoft.Extensions.DependencyInjection;
using Schemata.Flow.Integration.Tests.Fixtures;

namespace Schemata.Flow.Integration.Tests;

public sealed class LinqToDbFlowEffectRecoveryShould : FlowEffectRecoveryShould
{
    protected override IFlowIntegrationFixture CreateFixture(FakeExternalSystem external) {
        return new LinqToDbFlowFixture { ConfigureServices = services => {
            services.AddSingleton(external);
            services.AddSchemataFlowRepositoryEffectRecorder();
        } };
    }
}
