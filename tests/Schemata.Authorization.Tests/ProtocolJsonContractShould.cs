using System.Text.Json;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Core.Json;
using Xunit;

namespace Schemata.Authorization.Tests;

/// <summary>
///     Behavioral coverage for protocol numeric fields under the shared ambient JSON
///     configuration, per issue #87: the owning protocol members serialize as JSON numbers
///     even when the global long-to-string coercion is active, resource long precision stays
///     stringified, and SnakeCaseLower naming is preserved.
/// </summary>
public class ProtocolJsonContractShould
{
    private static readonly JsonSerializerOptions Ambient = new() {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters           = { JsonStringNumberConverter.Instance },
    };

    [Fact]
    public void Introspection_Dates_Are_Numbers_Under_The_Ambient_Long_To_String_Options() {
        var response = new IntrospectionResponse {
            Active = true, Exp = 1_800_000_000, Iat = 1_700_000_000, Nbf = 1_700_000_100, AuthTime = 1_699_999_900,
        };

        var json = JsonSerializer.Serialize(response, Ambient);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(JsonValueKind.Number, root.GetProperty("exp").ValueKind);
        Assert.Equal(JsonValueKind.Number, root.GetProperty("iat").ValueKind);
        Assert.Equal(JsonValueKind.Number, root.GetProperty("nbf").ValueKind);
        Assert.Equal(JsonValueKind.Number, root.GetProperty("auth_time").ValueKind);
        Assert.Equal(1_800_000_000, root.GetProperty("exp").GetInt64());
    }

    [Fact]
    public void Registration_Dates_Are_Numbers_Under_The_Ambient_Long_To_String_Options() {
        var response = new RegistrationResponse {
            ClientId = "client-1", ClientIdIssuedAt = 1_700_000_000, ClientSecretExpiresAt = 0,
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(response, Ambient));
        var root = document.RootElement;
        Assert.Equal(JsonValueKind.Number, root.GetProperty("client_id_issued_at").ValueKind);
        Assert.Equal(0, root.GetProperty("client_secret_expires_at").GetInt64());
    }

    [Fact]
    public void Round_Trip_Accepts_Both_Number_And_String_Tokens() {
        const string json = "{\"exp\":1800000000,\"iat\":\"1700000000\"}";

        var response = JsonSerializer.Deserialize<IntrospectionResponse>(json, Ambient);

        Assert.NotNull(response);
        Assert.Equal(1_800_000_000, response.Exp);
        Assert.Equal(1_700_000_000, response.Iat);
    }

    [Fact]
    public void Default_Max_Age_Is_A_Number_On_Both_Registration_Shapes_Under_The_Ambient_Options() {
        var request  = new RegisterRequest { DefaultMaxAge = 900 };
        var response = new RegistrationResponse { ClientId = "client-1", DefaultMaxAge = 3600 };

        var requestJson  = JsonSerializer.Serialize(request, Ambient);
        var responseJson = JsonSerializer.Serialize(response, Ambient);

        using var requestDocument  = JsonDocument.Parse(requestJson);
        using var responseDocument = JsonDocument.Parse(responseJson);
        Assert.Equal(JsonValueKind.Number, requestDocument.RootElement.GetProperty("default_max_age").ValueKind);
        var maxAge = responseDocument.RootElement.GetProperty("default_max_age");
        Assert.Equal(JsonValueKind.Number, maxAge.ValueKind);
        Assert.Equal(3600, maxAge.GetInt64());
    }

    [Fact]
    public void Default_Max_Age_Round_Trips_A_Mirrored_String_Token() {
        var parsed = JsonSerializer.Deserialize<RegisterRequest>("{\"default_max_age\":\"900\"}", Ambient);

        Assert.NotNull(parsed);
        Assert.Equal(900, parsed.DefaultMaxAge);
    }

    [Fact]
    public void Jwks_Echoes_As_A_Raw_Json_Object_Under_The_Ambient_Options() {
        const string jwks = """{"keys":[{"kty":"RSA","kid":"rp-1","use":"sig","n":"x","e":"AQAB"}]}""";
        var response = new RegistrationResponse {
            ClientId = "client-1",
            Jwks     = JsonDocument.Parse(jwks).RootElement.Clone(),
        };

        var json = JsonSerializer.Serialize(response, Ambient);

        using var document = JsonDocument.Parse(json);
        var echoed = document.RootElement.GetProperty("jwks");
        Assert.Equal(JsonValueKind.Object, echoed.ValueKind);
        Assert.Equal(jwks, echoed.GetRawText());
    }

    [Fact]
    public void Cnf_And_Authorization_Details_Keep_Their_Object_And_Array_Shapes() {
        var response = new IntrospectionResponse {
            Active = true,
            Cnf    = JsonDocument.Parse("""{"jkt":"0ZcOCORZNYy-DWpqq30jZyJGHTN0d2HglBV3uiguA4I"}""").RootElement.Clone(),
            AuthorizationDetails = JsonDocument.Parse(
                """[{"type":"payment_initiation","actions":["initiate"]},{"type":"account_information","actions":["read"]}]""").RootElement.Clone(),
        };

        var json = JsonSerializer.Serialize(response, Ambient);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var cnf = root.GetProperty("cnf");
        Assert.Equal(JsonValueKind.Object, cnf.ValueKind);
        Assert.Equal("0ZcOCORZNYy-DWpqq30jZyJGHTN0d2HglBV3uiguA4I", cnf.GetProperty("jkt").GetString());
        var details = root.GetProperty("authorization_details");
        Assert.Equal(JsonValueKind.Array, details.ValueKind);
        Assert.Equal(2, details.GetArrayLength());
        Assert.Equal("payment_initiation", details[0].GetProperty("type").GetString());
        Assert.Equal("account_information", details[1].GetProperty("type").GetString());
    }

    private static readonly JsonSerializerOptions Naming = new() {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };
}
