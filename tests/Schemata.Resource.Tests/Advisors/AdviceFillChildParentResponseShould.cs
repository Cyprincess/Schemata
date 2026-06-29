using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Resource.Foundation.Advisors;
using Xunit;

namespace Schemata.Resource.Tests.Advisors;

public class AdviceFillChildParentResponseShould
{
    [Fact]
    public async Task NonChildDetail_LeavesAdvisorIdle() {
        var entity  = new Entity { CanonicalName = "tenants/t1/hosts/h1" };
        var detail  = new PlainDetail { CanonicalName = "tenants/t1/hosts/h1" };
        var advisor = new AdviceFillChildParentResponse<Entity, PlainDetail>();

        var result = await advisor.AdviseAsync(EmptyContext(), entity, detail, principal: null);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task EntityCanonical_IsPreferredSource() {
        var entity = new Entity { CanonicalName = "tenants/t1/hosts/h1" };
        var detail = new ChildDetail {
            CanonicalName = "tenants/STALE/hosts/h1",
        };
        var advisor = new AdviceFillChildParentResponse<Entity, ChildDetail>();

        await advisor.AdviseAsync(EmptyContext(), entity, detail, principal: null);

        Assert.Equal("tenants/t1", detail.Parent);
    }

    [Fact]
    public async Task NullEntity_FallsBack_To_DetailCanonical() {
        var detail = new ChildDetail { CanonicalName = "tenants/t1/hosts/h1" };
        var advisor = new AdviceFillChildParentResponse<Entity, ChildDetail>();

        await advisor.AdviseAsync(EmptyContext(), entity: null, detail, principal: null);

        Assert.Equal("tenants/t1", detail.Parent);
    }

    [Theory]
    [InlineData(null,                                       null)]
    [InlineData("",                                         null)]
    [InlineData("tenants/t1",                               null)]
    [InlineData("tenants/t1/hosts/h1",                      "tenants/t1")]
    [InlineData("organizations/o/projects/p/datasets/d",    "organizations/o/projects/p")]
    [InlineData("organizations/o/projects/p/datasets",      null)]
    public async Task DerivedParent_Matches_StripLastTwoSegments(string? canonical, string? expected) {
        var detail  = new ChildDetail { CanonicalName = canonical };
        var advisor = new AdviceFillChildParentResponse<Entity, ChildDetail>();

        await advisor.AdviseAsync(EmptyContext(), entity: null, detail, principal: null);

        Assert.Equal(expected, detail.Parent);
    }

    [Fact]
    public async Task Ensure_DoesNotOverwrite_IdenticalValue() {
        var entity = new Entity { CanonicalName = "tenants/t1/hosts/h1" };
        var detail = new ChildDetail {
            CanonicalName = "tenants/t1/hosts/h1",
            Parent        = "tenants/t1",
        };
        var advisor = new AdviceFillChildParentResponse<Entity, ChildDetail>();

        await advisor.AdviseAsync(EmptyContext(), entity, detail, principal: null);

        Assert.Equal("tenants/t1", detail.Parent);
    }

    private static AdviceContext EmptyContext() {
        return new(new ServiceCollection().BuildServiceProvider());
    }

    #region Fixtures

    public sealed class Entity : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class PlainDetail : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class ChildDetail : ICanonicalName, IChild
    {
        public string? Parent { get; set; }

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    #endregion
}
