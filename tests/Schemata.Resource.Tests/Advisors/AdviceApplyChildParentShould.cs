using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Resource.Foundation.Advisors;
using Xunit;

namespace Schemata.Resource.Tests.Advisors;

public class AdviceApplyChildParentShould
{
    [Fact]
    public async Task NonChildRequest_IsNoOp() {
        var entity  = new HostEntity { Tenant = "preset", Name = "h" };
        var request = new PlainRequest { Name = "h" };
        var advisor = new AdviceApplyChildParent<HostEntity, PlainRequest>();

        var result = await advisor.AdviseAsync(EmptyContext(), request, entity, null);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Equal("preset", entity.Tenant);
    }

    [Fact]
    public async Task BlankParent_IsNoOp() {
        var entity  = new HostEntity { Tenant = "preset", Name = "h" };
        var request = new HostRequest { Parent = null, Name = "h" };
        var advisor = new AdviceApplyChildParent<HostEntity, HostRequest>();

        var result = await advisor.AdviseAsync(EmptyContext(), request, entity, null);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Equal("preset", entity.Tenant);
    }

    [Fact]
    public async Task WildcardParent_Throws_CrossParentUnsupported() {
        var entity  = new HostEntity { Tenant = "preset", Name = "h" };
        var request = new HostRequest { Parent = "tenants/-", Name = "h" };
        var advisor = new AdviceApplyChildParent<HostEntity, HostRequest>();

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => advisor.AdviseAsync(EmptyContext(), request, entity, null));

        Assert.NotNull(ex.Details);
        var violation = Assert.Single(ex.Details!,
                                      d => d is BadRequestDetail) as BadRequestDetail;
        Assert.NotNull(violation!.FieldViolations);
        var field = Assert.Single(violation.FieldViolations!);
        Assert.Equal(SchemataResources.CROSS_PARENT_UNSUPPORTED, field.Reason);
    }

    [Fact]
    public async Task MalformedParent_Throws_InvalidParent() {
        var entity  = new HostEntity { Tenant = "preset", Name = "h" };
        var request = new HostRequest { Parent = "wrong-collection/x", Name = "h" };
        var advisor = new AdviceApplyChildParent<HostEntity, HostRequest>();

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => advisor.AdviseAsync(EmptyContext(), request, entity, null));

        Assert.NotNull(ex.Details);
        var violation = Assert.Single(ex.Details!,
                                      d => d is BadRequestDetail) as BadRequestDetail;
        Assert.NotNull(violation!.FieldViolations);
        var field = Assert.Single(violation.FieldViolations!);
        Assert.Equal(SchemataResources.INVALID_PARENT, field.Reason);
    }

    [Fact]
    public async Task ValidParent_Writes_StructuralField() {
        var entity  = new HostEntity { Tenant = "preset", Name = "h" };
        var request = new HostRequest { Parent = "tenants/acme", Name = "h" };
        var advisor = new AdviceApplyChildParent<HostEntity, HostRequest>();

        var result = await advisor.AdviseAsync(EmptyContext(), request, entity, null);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Equal("acme", entity.Tenant);
    }

    private static AdviceContext EmptyContext() {
        return new(new ServiceCollection().BuildServiceProvider());
    }

    #region Fixtures

    [CanonicalName("tenants/{tenant}/hosts/{host}")]
    public sealed class HostEntity : ICanonicalName
    {
        public string? Tenant { get; set; }

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class HostRequest : ICanonicalName, IChild
    {
        public string? Parent { get; set; }

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class PlainRequest : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    #endregion
}
