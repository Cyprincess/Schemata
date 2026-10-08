using System;
using System.Text.Json;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Advisors;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class AuthorizationDetailsNarrowingShould
{
    private const string Original = """[{"type":"payment_initiation","actions":["list","read"]}]""";

    [Fact]
    public void Narrow_AbsentRequest_ReturnsAnIndependentCopyOfTheOriginal() {
        var descriptor = Descriptor("payment_initiation");
        var service = CreateService(descriptor.Object);

        var adopted = service.Narrow(Original, null);
        var repeated = service.Narrow(Original, "  ");

        Assert.Equal(Original, adopted.ToJsonString());
        Assert.Equal(Original, repeated.ToJsonString());
        Assert.NotSame(adopted, repeated);
        descriptor.Verify(d => d.Narrow(It.IsAny<JsonElement>(), It.IsAny<JsonElement>()), Times.Never);
    }

    [Fact]
    public void Narrow_NarrowingRequest_ReturnsTheActualDetailsInRequestOrder() {
        var service = CreateService(Descriptor("payment_initiation").Object);

        var actual = service.Narrow(
            Original,
            """[{"type":"payment_initiation","actions":["read"]},{"type":"payment_initiation","actions":["list"]}]""");

        Assert.Equal(2, actual.Count);
        Assert.Equal("read", actual[0]?["actions"]?[0]?.GetValue<string>());
        Assert.Equal("list", actual[1]?["actions"]?[0]?.GetValue<string>());
    }

    [Fact]
    public void Narrow_NarrowingRequest_DispatchesTheSameTypeOriginal() {
        JsonElement captured = default;
        var account = Descriptor("account_information");
        var payment = Descriptor(
            "payment_initiation",
            (granted, _) => {
                captured = granted.Clone();
                return null;
            });
        var service = CreateService(account.Object, payment.Object);

        Assert.Throws<OAuthException>(
            () => service.Narrow(
                """[{"type":"account_information","actions":["list_accounts"]},{"type":"payment_initiation","actions":["list","read"]}]""",
                """[{"type":"payment_initiation","actions":["list"]}]"""));

        Assert.Equal("payment_initiation", captured.GetProperty("type").GetString());
        account.Verify(d => d.Narrow(It.IsAny<JsonElement>(), It.IsAny<JsonElement>()), Times.Never);
    }

    [Fact]
    public void Narrow_RequestedRightsBeyondTheGrant_ThrowInvalidAuthorizationDetails() {
        var service = CreateService(Descriptor("payment_initiation", (_, _) => null).Object);

        var ex = Assert.Throws<OAuthException>(
            () => service.Narrow(Original, """[{"type":"payment_initiation","actions":["write"]}]"""));

        Assert.Equal(OAuthErrors.InvalidAuthorizationDetails, ex.Status);
    }

    [Fact]
    public void Narrow_RequestWithoutASameTypeOriginal_ThrowsInvalidAuthorizationDetails() {
        var service = CreateService(
            Descriptor("account_information").Object,
            Descriptor("payment_initiation").Object);

        var ex = Assert.Throws<OAuthException>(
            () => service.Narrow(Original, """[{"type":"account_information","actions":["list_accounts"]}]"""));

        Assert.Equal(OAuthErrors.InvalidAuthorizationDetails, ex.Status);
    }

    [Fact]
    public void Narrow_UnknownRequestedType_ThrowsInvalidAuthorizationDetails() {
        var service = CreateService(Descriptor("payment_initiation").Object);

        var ex = Assert.Throws<OAuthException>(() => service.Narrow(Original, """[{"type":"photos"}]"""));

        Assert.Equal(OAuthErrors.InvalidAuthorizationDetails, ex.Status);
    }

    [Fact]
    public void Narrow_AbsentOriginal_WithARequest_ThrowsInvalidAuthorizationDetails() {
        var service = CreateService(Descriptor("payment_initiation").Object);

        var ex = Assert.Throws<OAuthException>(
            () => service.Narrow(null, """[{"type":"payment_initiation","actions":["list"]}]"""));

        Assert.Equal(OAuthErrors.InvalidAuthorizationDetails, ex.Status);
    }

    [Fact]
    public void Narrow_MalformedRequest_ThrowsInvalidRequest() {
        var service = CreateService(Descriptor("payment_initiation").Object);

        var ex = Assert.Throws<OAuthException>(
            () => service.Narrow(Original, """{"type":"payment_initiation"}"""));

        Assert.Equal(OAuthErrors.InvalidRequest, ex.Status);
    }

    [Fact]
    public void Narrow_AfterNarrowing_TheOriginalGrantStaysIntact() {
        var service = CreateService(Descriptor("payment_initiation").Object);

        service.Narrow(Original, """[{"type":"payment_initiation","actions":["list"]}]""");
        var adopted = service.Narrow(Original, null);

        Assert.Equal(Original, adopted.ToJsonString());
    }

    private static AuthorizationDetailsService CreateService(params IAuthorizationDetailTypeDescriptor[] descriptors) {
        return new(descriptors);
    }

    private static Mock<IAuthorizationDetailTypeDescriptor> Descriptor(
        string type,
        Func<JsonElement, JsonElement, JsonElement?>? narrow = null) {
        var descriptor = new Mock<IAuthorizationDetailTypeDescriptor>();
        descriptor.SetupGet(d => d.Type).Returns(type);
        descriptor.Setup(d => d.Validate(It.IsAny<JsonElement>())).Returns((string?)null);
        descriptor.Setup(d => d.Narrow(It.IsAny<JsonElement>(), It.IsAny<JsonElement>()))
                  .Returns(
                       (JsonElement granted, JsonElement requested) => narrow is null
                           ? requested.Clone()
                           : narrow(granted, requested));
        return descriptor;
    }
}
