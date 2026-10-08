using Schemata.Flow.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Flow.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class LinqToDbSnapshotStampShould : SnapshotStampShould, IClassFixture<LinqToDbFlowFixture>
{
    public LinqToDbSnapshotStampShould(LinqToDbFlowFixture fixture) : base(fixture) { }
}
