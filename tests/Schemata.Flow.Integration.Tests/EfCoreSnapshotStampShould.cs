using Schemata.Flow.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Flow.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class EfCoreSnapshotStampShould : SnapshotStampShould, IClassFixture<EfCoreFlowFixture>
{
    public EfCoreSnapshotStampShould(EfCoreFlowFixture fixture) : base(fixture) { }
}
