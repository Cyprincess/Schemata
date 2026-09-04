using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Schemata.Authorization.Skeleton.Advisors;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Integration.Tests;

[Trait("Category", "Integration")]
[Trait("Layer", "Component")]
public class RichAuthorizationFlowShould
{
    /// <summary>The RFC 7636 Appendix B challenge for the fixed verifier.</summary>
    private const string Challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";
    private const string Details   = """[{"type":"payment_initiation","actions":["list"],"institutions":["cb"]}]""";
    private const string UnknownType = """[{"type":"account_information","actions":["list"]}]""";

    private readonly WebAppFactory _factory = new WebAppFactory().WithEnvironment("Rar");

    [Fact]
    public async Task Accept_The_Parameter_Under_The_Feature() {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync(Authorize(Details));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        var location = System.Web.HttpUtility.ParseQueryString(response.Headers.Location!.Query);
        Assert.Null(location[Parameters.Error]);
        Assert.False(string.IsNullOrWhiteSpace(location[Parameters.Code]));
    }

    [Fact]
    public async Task Reject_An_Unregistered_Type_Under_The_Feature() {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync(Authorize(UnknownType));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        Assert.Equal(OAuthErrors.InvalidAuthorizationDetails, error.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Ignore_The_Parameter_Without_The_Feature() {
        using var factory = new WebAppFactory();
        var       client  = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync(Authorize(Details));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        var location = System.Web.HttpUtility.ParseQueryString(response.Headers.Location!.Query);
        Assert.Null(location[Parameters.Error]);
        Assert.False(string.IsNullOrWhiteSpace(location[Parameters.Code]));
    }

    private static string Authorize(string details) {
        return "/connect/authorize?client_id=code-client"
             + "&redirect_uri=https%3A%2F%2Flocalhost%2Fcallback"
             + "&response_type=code"
             + "&scope=openid"
             + "&code_challenge=" + Challenge
             + "&code_challenge_method=S256"
             + "&authorization_details=" + Uri.EscapeDataString(details);
    }
}

internal sealed class PaymentInitiationDescriptor : IAuthorizationDetailTypeDescriptor
{
    public string Type => "payment_initiation";

    public string? Validate(JsonElement detail) { return null; }
}
