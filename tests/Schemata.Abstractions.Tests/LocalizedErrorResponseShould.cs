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

            var result = exception.CreateErrorResponse(locale: "zh-CN");
            Assert.NotNull(result);
            var response = Assert.IsType<ErrorResponse>(result);

            var error   = response.Error;
            Assert.NotNull(error);
            var details = error.Details;
            Assert.NotNull(details);
            var localized = Assert.Single(details.OfType<LocalizedMessageDetail>());
            Assert.Equal("zh-CN", localized.Locale);
            Assert.NotEqual(exception.Message, localized.Message);
        } finally {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void CreateErrorResponse_ForAbsentLocale_LeavesMessageUnlocalized() {
        var exception = new NotFoundException();

        var result = exception.CreateErrorResponse();
        Assert.NotNull(result);
        var response = Assert.IsType<ErrorResponse>(result);

        var error   = response.Error;
        Assert.NotNull(error);
        var details = error.Details;
        Assert.NotNull(details);
        Assert.Empty(details.OfType<LocalizedMessageDetail>());
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

        var result = exception.CreateErrorResponse(locale: "zh-CN");
        Assert.NotNull(result);
        var response = Assert.IsType<ErrorResponse>(result);

        var error   = response.Error;
        Assert.NotNull(error);
        var details = error.Details;
        Assert.NotNull(details);
        var badRequest = Assert.Single(details.OfType<BadRequestDetail>());
        Assert.NotNull(badRequest.FieldViolations);
        var violation = Assert.Single(badRequest.FieldViolations);
        Assert.NotNull(violation.LocalizedMessage);
        Assert.Equal("zh-CN", violation.LocalizedMessage.Locale);
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

        var result = exception.CreateErrorResponse(locale: "zh-CN");
        Assert.NotNull(result);
        var response = Assert.IsType<ErrorResponse>(result);

        var error   = response.Error;
        Assert.NotNull(error);
        var details = error.Details;
        Assert.NotNull(details);
        var badRequest = Assert.Single(details.OfType<BadRequestDetail>());
        Assert.NotNull(badRequest.FieldViolations);
        var violation = Assert.Single(badRequest.FieldViolations);
        Assert.Null(violation.LocalizedMessage);
    }
}
