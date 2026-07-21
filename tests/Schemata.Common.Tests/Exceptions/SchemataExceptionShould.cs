using System.Collections.Generic;
using System.Linq;
using Schemata.Abstractions;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Common.Tests.Exceptions;

public class SchemataExceptionShould
{
    [Fact]
    public void CreateErrorResponse_NullLocale_DoesNotAttachLocalizedMessage() {
        var exception = BuildException(SchemataResources.NOT_FOUND);

        var details = GetDetails(exception.CreateErrorResponse("rid", locale: null));

        Assert.DoesNotContain(details, d => d is LocalizedMessageDetail);
    }

    [Fact]
    public void CreateErrorResponse_EmptyLocale_DoesNotAttachLocalizedMessage() {
        var exception = BuildException(SchemataResources.NOT_FOUND);

        var details = GetDetails(exception.CreateErrorResponse("rid", locale: ""));

        Assert.DoesNotContain(details, d => d is LocalizedMessageDetail);
    }

    [Fact]
    public void CreateErrorResponse_UnknownReasonAndUnknownStatus_DoesNotAttachLocalizedMessage() {
        // EnsureLocalizedMessage falls back from Reason to Status when the reason has no resx
        // entry; both must miss before localization is skipped.
        var exception = new SchemataException(404, "STATUS_THAT_DOES_NOT_EXIST_IN_RESX") {
            Details = [new ErrorInfoDetail { Reason = "REASON_THAT_DOES_NOT_EXIST_IN_RESX" }],
        };

        var details = GetDetails(exception.CreateErrorResponse(locale: "en-US"));

        Assert.DoesNotContain(details, d => d is LocalizedMessageDetail);
    }

    [Fact]
    public void CreateErrorResponse_UnknownReason_FallsBackToStatusResx() {
        // Reason is absent from resx but Status (NOT_FOUND) is, so the fallback path
        // resolves the localized message from the status entry.
        var exception = BuildException("REASON_THAT_DOES_NOT_EXIST_IN_RESX");

        var details = GetDetails(exception.CreateErrorResponse(locale: "en-US"));

        var localized = Assert.Single(details.OfType<LocalizedMessageDetail>());
        Assert.Equal("en-US", localized.Locale);
        Assert.Equal("The requested resource was not found.", localized.Message);
    }

    [Fact]
    public void CreateErrorResponse_AlreadyContainsLocalizedMessage_DoesNotOverwrite() {
        var exception = new SchemataException(404, ErrorCodes.NotFound) {
            Details = [
                new ErrorInfoDetail { Reason = SchemataResources.NOT_FOUND },
                new LocalizedMessageDetail { Locale = "en-US", Message = "preset value" },
            ],
        };

        var details = GetDetails(exception.CreateErrorResponse(locale: "en-US"));

        var localized = details.OfType<LocalizedMessageDetail>().ToList();
        var single    = Assert.Single(localized);
        Assert.Equal("en-US", single.Locale);
        Assert.Equal("preset value", single.Message);
    }

    [Fact]
    public void CreateErrorResponse_InvalidLocale_DoesNotAttachLocalizedMessage() {
        var exception = BuildException(SchemataResources.NOT_FOUND);

        var details = GetDetails(exception.CreateErrorResponse(locale: "this-is-not-a-locale!"));

        Assert.DoesNotContain(details, d => d is LocalizedMessageDetail);
    }

    [Fact]
    public void CreateErrorResponse_KnownReasonAndEnUsLocale_AttachesEnglishMessage() {
        var exception = BuildException(SchemataResources.NOT_FOUND);

        var details = GetDetails(exception.CreateErrorResponse(locale: "en-US"));

        var localized = Assert.Single(details.OfType<LocalizedMessageDetail>());
        Assert.Equal("en-US", localized.Locale);
        Assert.Equal("The requested resource was not found.", localized.Message);
    }

    [Fact]
    public void CreateErrorResponse_TemplatedReasonWithMetadata_FormatsMessage() {
        var exception = new SchemataException(400, ErrorCodes.InvalidArgument) {
            Details = [
                new ErrorInfoDetail {
                    Reason = SchemataResources.NOT_EMPTY,
                    Metadata = new() {
                        ["field"] = "Title",
                    },
                },
            ],
        };

        var details = GetDetails(exception.CreateErrorResponse(locale: "en-US"));

        var localized = Assert.Single(details.OfType<LocalizedMessageDetail>());
        Assert.Equal("'Title' must not be empty.", localized.Message);
    }

    [Fact]
    public void CreateErrorResponse_NamedPlaceholders_ResolveByMetadataKey() {
        var exception = new SchemataException(400, ErrorCodes.InvalidArgument) {
            Details = [
                new ErrorInfoDetail {
                    Reason   = SchemataResources.INVALID_UPDATE_MASK,
                    Metadata = new() {
                        ["reason"] = "it is read-only",
                        ["path"]   = "title",
                    },
                },
            ],
        };

        var details = GetDetails(exception.CreateErrorResponse(locale: "en-US"));

        var localized = Assert.Single(details.OfType<LocalizedMessageDetail>());
        Assert.Equal("The update_mask path 'title' is invalid: it is read-only.", localized.Message);
    }

    [Fact]
    public void CreateErrorResponse_NamedPlaceholders_MissingKeyLeavesPlaceholderLiteral() {
        var exception = new SchemataException(400, ErrorCodes.InvalidArgument) {
            Details = [
                new ErrorInfoDetail {
                    Reason   = SchemataResources.INVALID_UPDATE_MASK,
                    Metadata = new() {
                        ["path"] = "title",
                    },
                },
            ],
        };

        var details = GetDetails(exception.CreateErrorResponse(locale: "en-US"));

        var localized = Assert.Single(details.OfType<LocalizedMessageDetail>());
        Assert.Equal("The update_mask path 'title' is invalid: {reason}.", localized.Message);
    }

    [Fact]
    public void CreateErrorResponse_PositionalTemplate_UsesMetadataInsertionOrder() {
        var exception = new SchemataException(404, ErrorCodes.NotFound) {
            Details = [
                new ErrorInfoDetail {
                    Reason   = SchemataResources.NAMED_NOT_FOUND,
                    Metadata = new() {
                        ["type"] = "Book",
                        ["name"] = "Hugo",
                    },
                },
            ],
        };

        var details = GetDetails(exception.CreateErrorResponse(locale: "en-US"));

        var localized = Assert.Single(details.OfType<LocalizedMessageDetail>());
        Assert.Equal("Book 'Hugo' not found.", localized.Message);
    }

    [Fact]
    public void CreateErrorResponse_MissingErrorInfo_LocalizesAutoInsertedInternal() {
        var exception = new SchemataException(500, ErrorCodes.Internal);
        exception.Details = [];

        var details = GetDetails(exception.CreateErrorResponse(locale: "en-US"));

        var info = Assert.Single(details.OfType<ErrorInfoDetail>());
        Assert.Equal(ErrorCodes.Internal, info.Reason);

        var localized = Assert.Single(details.OfType<LocalizedMessageDetail>());
        Assert.Equal("en-US", localized.Locale);
        Assert.Equal("An internal server error occurred.", localized.Message);
    }

    private static SchemataException BuildException(string reason) {
        return new(404, ErrorCodes.NotFound) {
            Details = [new ErrorInfoDetail { Reason = reason }],
        };
    }

    private static List<IErrorDetail> GetDetails(object? response) {
        var body = Assert.IsType<ErrorResponse>(response);
        Assert.NotNull(body.Error);
        Assert.NotNull(body.Error!.Details);
        return body.Error.Details!;
    }
}
