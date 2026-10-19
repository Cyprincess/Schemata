using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Advisors;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class AuthorizationDetailsServiceShould
{
    [Fact]
    public void Parse_AbsentParameter_ReturnsEmptyArray() {
        var service = CreateService(Descriptor("payment_initiation"));

        Assert.Empty(service.Parse(null));
        Assert.Empty(service.Parse("   "));
    }

    [Fact]
    public void Parse_EmptyArray_ReturnsEmptyArray() {
        var service = CreateService(Descriptor("payment_initiation"));

        Assert.Empty(service.Parse("[]"));
    }

    [Fact]
    public void Parse_TwoRegisteredTypes_ReturnsNormalizedArray() {
        var service = CreateService(
            Descriptor("account_information"),
            Descriptor(
                "payment_initiation",
                e => e.TryGetProperty("instructedAmount", out _) ? null : "The instructedAmount member is required."));

        var details = service.Parse(
            """[{"type":"account_information","actions":["list_accounts"]},{"type":"payment_initiation","instructedAmount":{"currency":"EUR","amount":"123.50"}}]""");

        Assert.Equal(2, details.Count);
        Assert.Equal("account_information", details[0]?["type"]?.GetValue<string>());
        Assert.Equal("payment_initiation", details[1]?["type"]?.GetValue<string>());
        Assert.Equal("123.50", details[1]?["instructedAmount"]?["amount"]?.GetValue<string>());
    }

    [Fact]
    public void Parse_MalformedJson_ThrowsInvalidRequest() {
        var service = CreateService(Descriptor("payment_initiation"));

        var ex = Assert.Throws<OAuthException>(
            () => service.Parse("""[{"type":"""));

        Assert.Equal(OAuthErrors.InvalidRequest, ex.Status);
    }

    [Fact]
    public void Parse_NonArrayRoot_ThrowsInvalidRequest() {
        var service = CreateService(Descriptor("payment_initiation"));

        var ex = Assert.Throws<OAuthException>(
            () => service.Parse("""{"type":"payment_initiation"}"""));

        Assert.Equal(OAuthErrors.InvalidRequest, ex.Status);
    }

    [Fact]
    public void Parse_ElementNotObject_ThrowsInvalidRequest() {
        var service = CreateService(Descriptor("payment_initiation"));

        var ex = Assert.Throws<OAuthException>(() => service.Parse("""["payment_initiation"]"""));

        Assert.Equal(OAuthErrors.InvalidRequest, ex.Status);
    }

    [Fact]
    public void Parse_MissingTypeMember_ThrowsInvalidRequest() {
        var service = CreateService(Descriptor("payment_initiation"));

        var ex = Assert.Throws<OAuthException>(
            () => service.Parse("""[{"instructedAmount":{"currency":"EUR","amount":"123.50"}}]"""));

        Assert.Equal(OAuthErrors.InvalidRequest, ex.Status);
    }

    [Fact]
    public void Parse_NonStringTypeMember_ThrowsInvalidRequest() {
        var service = CreateService(Descriptor("payment_initiation"));

        var ex = Assert.Throws<OAuthException>(() => service.Parse("""[{"type":42}]"""));

        Assert.Equal(OAuthErrors.InvalidRequest, ex.Status);
    }

    [Fact]
    public void Parse_UnregisteredType_ThrowsInvalidAuthorizationDetails() {
        var service = CreateService(Descriptor("payment_initiation"));

        var ex = Assert.Throws<OAuthException>(() => service.Parse("""[{"type":"photos"}]"""));

        Assert.Equal(OAuthErrors.InvalidAuthorizationDetails, ex.Status);
        Assert.Contains("photos", ex.Message);
    }

    [Fact]
    public void Parse_NoRegisteredTypes_RejectsEveryType() {
        var service = CreateService();

        var ex = Assert.Throws<OAuthException>(
            () => service.Parse("""[{"type":"payment_initiation"}]"""));

        Assert.Equal(OAuthErrors.InvalidAuthorizationDetails, ex.Status);
    }

    [Fact]
    public void Parse_DescriptorRejection_ThrowsInvalidAuthorizationDetailsWithMessage() {
        const string message = "The instructedAmount member is required.";
        var service = CreateService(Descriptor("payment_initiation", _ => message));

        var ex = Assert.Throws<OAuthException>(
            () => service.Parse("""[{"type":"payment_initiation"}]"""));

        Assert.Equal(OAuthErrors.InvalidAuthorizationDetails, ex.Status);
        Assert.Equal(message, ex.Message);
    }

    [Theory]
    [InlineData("""[{"type":""", OAuthErrors.InvalidRequest)]
    [InlineData("""{"type":"payment_initiation"}""", OAuthErrors.InvalidRequest)]
    [InlineData("""["payment_initiation"]""", OAuthErrors.InvalidRequest)]
    [InlineData("""[{"actions":[]}]""", OAuthErrors.InvalidRequest)]
    [InlineData("""[{"type":42}]""", OAuthErrors.InvalidRequest)]
    [InlineData("""[{"type":"photos"}]""", OAuthErrors.InvalidAuthorizationDetails)]
    [InlineData("""[{"type":"payment_initiation"}]""", OAuthErrors.InvalidAuthorizationDetails)]
    public void Parse_WhenInvalid_RepeatedlyThrowsTheDocumentedStatus(string raw, string expected) {
        var rejecting = CreateService(Descriptor("payment_initiation", _ => "Rejected."));

        for (var iteration = 0; iteration < 10; iteration++) {
            var ex = Assert.Throws<OAuthException>(() => rejecting.Parse(raw));
            Assert.Equal(expected, ex.Status);
        }
    }

    [Fact]
    public void Parse_ReturnsClonedArrayUsableAfterReturn() {
        const string raw = """[{"type":"payment_initiation","actions":["list","read"],"instructedAmount":{"currency":"EUR","amount":"123.50"}}]""";
        var service = CreateService(Descriptor("payment_initiation"));

        var details = service.Parse(raw);
        var repeated = service.Parse(raw);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.Equal(raw, details.ToJsonString());
        Assert.Equal(raw, repeated.ToJsonString());
        Assert.NotSame(details, repeated);

        ((JsonArray)details[0]!["actions"]!).Add("write");

        Assert.Equal(3, details[0]?["actions"]?.AsArray().Count);
        Assert.Equal(2, repeated[0]?["actions"]?.AsArray().Count);
        Assert.Equal("list", repeated[0]?["actions"]?[0]?.GetValue<string>());
        Assert.Equal("read", repeated[0]?["actions"]?[1]?.GetValue<string>());
    }

    [Fact]
    public void Create_DuplicateTypeRegistrations_FailFast() {
        Assert.Throws<ArgumentException>(
            () => CreateService(Descriptor("payment_initiation"), Descriptor("payment_initiation")));
    }

    private static AuthorizationDetailsService CreateService(params IAuthorizationDetailTypeDescriptor[] descriptors) {
        return new(descriptors);
    }

    private static IAuthorizationDetailTypeDescriptor Descriptor(
        string                      type,
        Func<JsonElement, string?>? validate = null) {
        var descriptor = new Mock<IAuthorizationDetailTypeDescriptor>();
        descriptor.SetupGet(d => d.Type).Returns(type);
        descriptor.Setup(d => d.Validate(It.IsAny<JsonElement>())).Returns(validate ?? (_ => null));

        return descriptor.Object;
    }
}
