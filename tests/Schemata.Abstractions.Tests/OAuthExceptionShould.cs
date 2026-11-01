using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Tests;

public partial class OAuthExceptionShould
{
    // RFC 6749 §5.2 error_description vocabulary: %x20-21 / %x23-5B / %x5D-7E.
    [GeneratedRegex("^[\\x20-\\x21\\x23-\\x5B\\x5D-\\x7E]*$")]
    private static partial Regex AllowedDescriptionCharacters();

    [Fact]
    public void Constructor_FromResourceKey_RendersInvariantDeveloperText_RegardlessOfUICulture() {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
        try {
            var exception = new OAuthException(
                "invalid_grant",
                SchemataResources.NOT_EMPTY,
                new Dictionary<string, string?> { ["value"] = "code_challenge" });

            Assert.Equal("'code_challenge' must not be empty.", exception.Message);
        } finally {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void CreateErrorResponse_ForRequestedLocale_RendersLocalizedDetailFromSameFacts() {
        var exception = new OAuthException(
            "invalid_grant",
            SchemataResources.PKCE_VERIFIER_MISMATCH);

        var result = exception.CreateErrorResponse(locale: "zh-CN");
        var response = Assert.IsType<OAuthErrorResponse>(result);

        var localized = Assert.Single(response.Details!.OfType<LocalizedMessageDetail>());
        Assert.Equal("zh-CN", localized.Locale);
        Assert.NotEqual(exception.Message, localized.Message);
    }

    [Fact]
    public void CreateErrorResponse_DescriptionWithDisallowedCharacters_StaysWithinRfc6749Vocabulary() {
        var exception = OAuthException.FromDescription(
            "invalid_grant",
            "quote \" backslash \\ unicode 中文 emoji \uD83D\uDE00 end");

        var response = Assert.IsType<OAuthErrorResponse>(exception.CreateErrorResponse());

        Assert.Matches(AllowedDescriptionCharacters(), response.ErrorDescription);
        Assert.Contains("%22", response.ErrorDescription);
        Assert.Contains("%5C", response.ErrorDescription);
        Assert.Contains("%E4%B8%AD", response.ErrorDescription);
        Assert.Contains("%F0%9F%98%80", response.ErrorDescription);
    }

    [Fact]
    public void CreateErrorResponse_FromDescription_InsertsErrorInfoWithExceptionDomain() {
        var exception = OAuthException.FromDescription("invalid_grant", "denied");

        var response = Assert.IsType<OAuthErrorResponse>(exception.CreateErrorResponse());

        var info = Assert.Single(response.Details!.OfType<ErrorInfoDetail>());
        Assert.Equal(ErrorDomains.OAuth, info.Domain);
    }

    [Fact]
    public void CreateErrorResponse_ExistingErrorInfoWithoutDomain_ReceivesExceptionDomain() {
        var exception = new SchemataException(400, ErrorCodes.FailedPrecondition) {
            Details = [new ErrorInfoDetail { Reason = "MIGRATION_REQUIRED" }],
        };

        var response = Assert.IsType<ErrorResponse>(exception.CreateErrorResponse());

        var info = Assert.Single(response.Error!.Details!.OfType<ErrorInfoDetail>());
        Assert.Equal(ErrorDomains.Schemata, info.Domain);
    }

    [Fact]
    public void CreateErrorResponse_ExistingErrorInfoWithDomain_KeepsExplicitDomain() {
        var exception = new SchemataException(403, ErrorCodes.PermissionDenied) {
            Details = [new ErrorInfoDetail { Reason = "CUSTOM", Domain = "consumer.example.com" }],
        };

        var response = Assert.IsType<ErrorResponse>(exception.CreateErrorResponse());

        var info = Assert.Single(response.Error!.Details!.OfType<ErrorInfoDetail>());
        Assert.Equal("consumer.example.com", info.Domain);
    }
}
