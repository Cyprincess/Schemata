using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Xunit;

namespace Schemata.Abstractions.Tests;

[Trait("Category", "Unit")]
public sealed class FrameworkTemplateShould
{
    [Trait("Layer", "Unit")]
    [Theory]
    [InlineData("en-US")]
    [InlineData("fr")]
    public void VersionRegistrationFailure_RendersNamedArguments_AndRetainsBusinessReason(string locale) {
        var error = new FailedPreconditionException(SchemataResources.FLOW_DEFINITION_VERSION_NOT_REGISTERED,
            new Dictionary<string, string?> { ["version"] = "2", ["name"] = "approval" });
        var response = Assert.IsType<ErrorResponse>(error.CreateErrorResponse(locale: locale));
        Assert.Equal("Process definition 'approval@2' is not registered.", response.Error!.Message);
        Assert.Equal("FAILED_PRECONDITION", response.Error.Status);
        var details = response.Error.Details;
        Assert.NotNull(details);
        var info = Assert.Single(details.OfType<ErrorInfoDetail>());
        Assert.Equal(SchemataResources.FLOW_DEFINITION_VERSION_NOT_REGISTERED, info.Reason);
        Assert.Equal("approval", info.Metadata!["name"]);
        Assert.Equal("2", info.Metadata["version"]);
        Assert.Equal("Process definition 'approval@2' is not registered.", Assert.Single(details.OfType<LocalizedMessageDetail>()).Message);
    }

    [Trait("Layer", "Unit")]
    [Theory]
    [InlineData("FLOW_DEFINITION_VERSION_REQUIRED", "An exact process definition version is required.")]
    [InlineData("FLOW_PARTICIPANT_INVALID", "A participant subject and valid participation kind are required.")]
    [InlineData("FLOW_PARTICIPATION_PROCESS_ONLY", "Historical participation belongs to the process.")]
    public void FrameworkRejection_RendersBusinessTemplate_InvariantUnderSatelliteCulture(string key, string expected) {
        var previous = CultureInfo.CurrentUICulture;
        try {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr");
            var error = new InvalidArgumentException(key);
            var response = Assert.IsType<ErrorResponse>(error.CreateErrorResponse(locale: "fr"));
            Assert.Equal(expected, response.Error!.Message);
            var details = response.Error.Details;
            Assert.NotNull(details);
            Assert.Equal(key, Assert.Single(details.OfType<ErrorInfoDetail>()).Reason);
            Assert.Equal(expected, Assert.Single(details.OfType<LocalizedMessageDetail>()).Message);
        } finally { CultureInfo.CurrentUICulture = previous; }
    }
}
