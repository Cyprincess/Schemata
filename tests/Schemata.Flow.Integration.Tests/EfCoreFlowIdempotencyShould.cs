using Schemata.Flow.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Flow.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class EfCoreFlowIdempotencyShould : FlowIdempotencyShould, IClassFixture<EfCoreFlowFixture>
{
    public EfCoreFlowIdempotencyShould(EfCoreFlowFixture fixture) : base(fixture) { }
}