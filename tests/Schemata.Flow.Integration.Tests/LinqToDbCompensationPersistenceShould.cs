using Schemata.Flow.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Flow.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class LinqToDbCompensationPersistenceShould : CompensationPersistenceShould, IClassFixture<LinqToDbFlowFixture>
{
    public LinqToDbCompensationPersistenceShould(LinqToDbFlowFixture fixture) : base(fixture) { }
}