using Schemata.Flow.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Flow.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class EfCoreCompensationPersistenceShould : CompensationPersistenceShould, IClassFixture<EfCoreFlowFixture>
{
    public EfCoreCompensationPersistenceShould(EfCoreFlowFixture fixture) : base(fixture) { }
}