using System.Globalization;
using System.Linq;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Xunit;

namespace Schemata.Abstractions.Tests;

public class LocalizedErrorResponseShould
{
    [Fact]
    public void CreateErrorResponse_ForLocaleWithTemplate_AttachesLocalizedMessage() {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        try {
            var exception = new NotFoundException();

            var response = (ErrorResponse)exception.CreateErrorResponse(locale: "zh-CN")!;

            var localized = response.Error!.Details!.OfType<LocalizedMessageDetail>().Single();
            Assert.Equal("zh-CN", localized.Locale);
            Assert.NotEqual(exception.Message, localized.Message);
        } finally {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void CreateErrorResponse_ForAbsentLocale_LeavesMessageUnlocalized() {
        var exception = new NotFoundException();

        var response = (ErrorResponse)exception.CreateErrorResponse()!;

        Assert.Empty(response.Error!.Details!.OfType<LocalizedMessageDetail>());
    }

    [Fact]
    public void CreateErrorResponse_ForFieldViolationWithoutPlaceholders_FillsLocalizedMessage() {
        var exception = new ValidationException([
            new() {
                Field       = "page_token",
                Reason      = SchemataResources.INVALID_PAGE_TOKEN,
                Description = "The page token is invalid or has expired.",
            },
        ]);

        var response = (ErrorResponse)exception.CreateErrorResponse(locale: "zh-CN")!;

        var violation = response.Error!.Details!.OfType<BadRequestDetail>()
                                 .Single()
                                 .FieldViolations!
                                 .Single();
        Assert.NotNull(violation.LocalizedMessage);
        Assert.Equal("zh-CN", violation.LocalizedMessage!.Locale);
        Assert.NotEqual(violation.Description, violation.LocalizedMessage.Message);
    }

    [Fact]
    public void CreateErrorResponse_ForFieldViolationWithPlaceholders_LeavesLocalizedMessageEmpty() {
        var exception = new ValidationException([
            new() {
                Field       = "name",
                Reason      = SchemataResources.NOT_EMPTY,
                Description = "'Name' must not be empty.",
            },
        ]);

        var response = (ErrorResponse)exception.CreateErrorResponse(locale: "zh-CN")!;

        var violation = response.Error!.Details!.OfType<BadRequestDetail>()
                                 .Single()
                                 .FieldViolations!
                                 .Single();
        Assert.Null(violation.LocalizedMessage);
    }
}
