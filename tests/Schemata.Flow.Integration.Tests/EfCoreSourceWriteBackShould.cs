using Schemata.Flow.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Flow.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class EfCoreSourceWriteBackShould : SourceWriteBackShould, IClassFixture<EfCoreFlowFixture>
{
    public EfCoreSourceWriteBackShould(EfCoreFlowFixture fixture) : base(fixture) { }
}