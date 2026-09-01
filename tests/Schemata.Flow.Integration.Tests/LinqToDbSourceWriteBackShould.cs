using Schemata.Flow.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Flow.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class LinqToDbSourceWriteBackShould : SourceWriteBackShould, IClassFixture<LinqToDbFlowFixture>
{
    public LinqToDbSourceWriteBackShould(LinqToDbFlowFixture fixture) : base(fixture) { }
}