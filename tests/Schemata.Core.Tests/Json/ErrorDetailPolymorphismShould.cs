using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Errors;
using Xunit;

namespace Schemata.Core.Tests.Json;

public class ErrorDetailPolymorphismShould
{
    private static JsonSerializerOptions SchemataJsonOptions() {
        var builder = WebApplication.CreateBuilder();
        builder.UseSchemata(schema => schema.UseJsonSerializer());

        var app = builder.Build();
        return app.Services.GetRequiredService<IOptions<JsonSerializerOptions>>().Value;
    }

    private static ErrorResponse Response(string reason) {
        return new() {
            Error = new() {
                Code    = 412,
                Message = "The request cannot be executed in the current system state.",
                Status  = "FAILED_PRECONDITION",
                Details = [new ErrorInfoDetail { Reason = reason, Domain = "loop" }],
            },
        };
    }

    [Fact]
    public void Serialize_ErrorInfoDetail_EmitsReasonAndDiscriminator() {
        var options = SchemataJsonOptions();

        var json = JsonSerializer.Serialize(Response("TICKET_ALREADY_OPEN"), options);

        Assert.Contains("TICKET_ALREADY_OPEN", json);
        Assert.Contains("@type", json);
    }

    [Fact]
    public void RoundTrip_ErrorInfoDetail_PreservesConcreteTypeAndReason() {
        var options = SchemataJsonOptions();

        var json  = JsonSerializer.Serialize(Response("TICKET_ALREADY_OPEN"), options);
        var round = JsonSerializer.Deserialize<ErrorResponse>(json, options);

        Assert.NotNull(round);
        var body = round.Error;
        Assert.NotNull(body);
        var details = body.Details;
        Assert.NotNull(details);
        var detail = Assert.Single(details);
        var info   = Assert.IsType<ErrorInfoDetail>(detail);
        Assert.Equal("TICKET_ALREADY_OPEN", info.Reason);
        Assert.Equal("loop", info.Domain);
    }
}
