using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Foundation.Features;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class RichAuthorizationShould
{
    private const string PaymentDetails = """
        [{"type":"payment_initiation","actions":["initiate"],"institutions":["cb"],"instruction":{"type":"instant"}},{"type":"account_information","actions":["list"],"locations":["https://rs.example"]}]

        """;

    private static (AdviceAuthorizeAuthorizationDetails<SchemataApplication> Advisor, AdviceContext Ctx) CreateAdvisor(
        IEnumerable<IAuthorizationDetailTypeDescriptor>? descriptors = null
    ) {
        var service = new AuthorizationDetailsService(descriptors ?? []);
        var advisor = new AdviceAuthorizeAuthorizationDetails<SchemataApplication>(service);
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        return (advisor, ctx);
    }

    private static AuthorizeContext<SchemataApplication> Context(string? details, ICollection<string>? clientTypes = null) {
        return new() {
            Request = new() {
                ClientId            = "client-1",
                RedirectUri         = "https://rp.example/cb",
                ResponseType        = "code",
                AuthorizationDetails = details,
            },
            Application = new() {
                ClientId                  = "client-1",
                AuthorizationDetailsTypes = clientTypes,
            },
        };
    }

    private static IAuthorizationDetailTypeDescriptor Descriptor(params string[] types) {
        var descriptor = new Mock<IAuthorizationDetailTypeDescriptor>();
        descriptor.Setup(d => d.Type).Returns(types[0]);
        descriptor.Setup(d => d.Validate(It.IsAny<System.Text.Json.JsonElement>())).Returns((string?)null);
        return descriptor.Object;
    }

    [Fact]
    public async Task Pass_Through_When_The_Parameter_Is_Absent() {
        var (advisor, ctx) = CreateAdvisor();

        var result = await advisor.AdviseAsync(ctx, Context(null), CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.False(ctx.TryGet<AuthorizationDetailsGrant>(out var _));
    }

    [Fact]
    public async Task Publish_The_Normalized_Grant_Set_On_The_Context() {
        var (advisor, ctx) = CreateAdvisor([Descriptor("payment_initiation"), Descriptor("account_information")]);

        var result = await advisor.AdviseAsync(ctx, Context(PaymentDetails, ["payment_initiation", "account_information"]), CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.True(ctx.TryGet<AuthorizationDetailsGrant>(out var grant));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(grant!.Json), JsonNode.Parse(PaymentDetails)));
    }

    [Fact]
    public async Task Reject_An_Unregistered_Type() {
        var (advisor, ctx) = CreateAdvisor([Descriptor("account_information")]);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => advisor.AdviseAsync(ctx, Context(PaymentDetails, ["payment_initiation", "account_information"]), CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidAuthorizationDetails, ex.Status);
        Assert.False(ctx.TryGet<AuthorizationDetailsGrant>(out var _));
    }

    [Fact]
    public async Task Reject_A_Type_Outside_The_Client_Registered_Set() {
        var (advisor, ctx) = CreateAdvisor([Descriptor("payment_initiation"), Descriptor("account_information")]);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => advisor.AdviseAsync(ctx, Context(PaymentDetails, ["account_information"]), CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequest, ex.Status);
        Assert.False(ctx.TryGet<AuthorizationDetailsGrant>(out var _));
    }

    [Fact]
    public async Task Accept_Any_Registered_Type_When_The_Client_Declares_None() {
        var (advisor, ctx) = CreateAdvisor([Descriptor("payment_initiation"), Descriptor("account_information")]);

        var result = await advisor.AdviseAsync(ctx, Context(PaymentDetails, null), CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.True(ctx.TryGet<AuthorizationDetailsGrant>(out var _));
    }

    [Fact]
    public void Register_The_Advisors_And_The_Details_Service() {
        var services = new ServiceCollection();

        new RichAuthorizationFeature<SchemataApplication>().ConfigureServices(services, new(), new());

        using var provider = services.BuildServiceProvider();

        Assert.Contains(
            provider.GetRequiredService<IEnumerable<IAuthorizeAdvisor<SchemataApplication>>>(),
            advisor => advisor is AdviceAuthorizeAuthorizationDetails<SchemataApplication>);
        Assert.Contains(
            provider.GetRequiredService<IEnumerable<IIntrospectionAdvisor<SchemataApplication>>>(),
            advisor => advisor is AdviceIntrospectionAuthorizationDetails<SchemataApplication>);
        Assert.Contains(
            provider.GetRequiredService<IEnumerable<IDiscoveryAdvisor>>(),
            advisor => advisor is AdviceDiscoveryRichAuthorization);
        Assert.NotNull(provider.GetRequiredService<AuthorizationDetailsService>());
    }

    [Fact]
    public async Task Advertise_Registered_Types_In_Discovery() {
        var advisor = new AdviceDiscoveryRichAuthorization([Descriptor("payment_initiation"), Descriptor("account_information")]);
        var discovery = new DiscoveryContext { Issuer = "https://as.example" };

        await advisor.AdviseAsync(new(null!), discovery, CancellationToken.None);

        Assert.NotNull(discovery.Document);
        Assert.Equal(["payment_initiation", "account_information"], discovery.Document!.AuthorizationDetailsTypesSupported);
    }

    [Fact]
    public async Task Omit_The_Discovery_Field_When_No_Descriptor_Is_Registered() {
        var advisor = new AdviceDiscoveryRichAuthorization([]);
        var discovery = new DiscoveryContext { Issuer = "https://as.example" };

        await advisor.AdviseAsync(new(null!), discovery, CancellationToken.None);

        Assert.Null(discovery.Document?.AuthorizationDetailsTypesSupported);
    }
}
