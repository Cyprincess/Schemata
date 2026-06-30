using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Resource.Foundation.Advisors;
using Xunit;

namespace Schemata.Resource.Tests.Advisors;

public class AdviceListResponseParentShould
{
    [Fact]
    public async Task NonChildSummary_ShortCircuits() {
        ImmutableArray<PlainSummary>? array =
        [
            new() { CanonicalName = "tenants/t1/hosts/h1" },
        ];
        var advisor = new AdviceListResponseParent<PlainSummary>();

        var result = await advisor.AdviseAsync(EmptyContext(), array, principal: null);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task NullArray_IsNoOp() {
        var advisor = new AdviceListResponseParent<ChildSummary>();

        var result = await advisor.AdviseAsync(EmptyContext(), summaries: null, principal: null);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task MixedCanonicalNames_DeriveParentForEach() {
        ImmutableArray<ChildSummary>? array =
        [
            new() { CanonicalName = "tenants/t1/hosts/h1" },
            new() { CanonicalName = "tenants/t2/hosts/h2" },
            new() { CanonicalName = "tenants/t3" },
        ];
        var advisor = new AdviceListResponseParent<ChildSummary>();

        await advisor.AdviseAsync(EmptyContext(), array, principal: null);

        Assert.Equal("tenants/t1", array.Value[0].Parent);
        Assert.Equal("tenants/t2", array.Value[1].Parent);
        Assert.Null(array.Value[2].Parent);
    }

    [Fact]
    public async Task Mutation_HappensInPlace() {
        var original = ImmutableArray.Create(
            new ChildSummary { CanonicalName = "tenants/t1/hosts/h1" });
        ImmutableArray<ChildSummary>? input = original;
        var advisor = new AdviceListResponseParent<ChildSummary>();

        await advisor.AdviseAsync(EmptyContext(), input, principal: null);

        Assert.Same(original[0], input.Value[0]);
        Assert.Equal("tenants/t1", original[0].Parent);
    }

    private static AdviceContext EmptyContext() {
        return new(new ServiceCollection().BuildServiceProvider());
    }

    #region Fixtures

    public sealed class PlainSummary : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class ChildSummary : ICanonicalName, IChild
    {
        public string? Parent { get; set; }

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    #endregion
}
