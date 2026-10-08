using System.Collections.Generic;
using Schemata.Abstractions.Exceptions;
using Schemata.Security.Foundation.Signatures;
using Xunit;

namespace Schemata.Security.Tests;

public class SignatureBaseShould
{
    // RFC 9421 appendix B.2: the shared test-request message.
    private static readonly string ContentDigest =
        "sha-512=:WZDPaVn/7XgHaAy8pmojAkGWoRx2UFChF41A2svX+TaPm+AbwAgBWnrIiYllu7BNNyealdVLvRwEmTHWXvJwew==:";

    [Fact]
    public void Build_The_Appendix_B21_Minimal_Base() {
        var signatureBase = SignatureBase.Create(
            TestRequest(),
            [],
            "();created=1618884473;keyid=\"test-key-rsa-pss\";nonce=\"b3k2pp5k7z-50gnwp.yemd\""
        );

        Assert.Equal(
            "\"@signature-params\": ();created=1618884473;keyid=\"test-key-rsa-pss\";nonce=\"b3k2pp5k7z-50gnwp.yemd\"",
            signatureBase
        );
    }

    [Fact]
    public void Build_The_Appendix_B22_Selective_Base() {
        var components = new List<MessageComponentIdentifier> {
            MessageComponentIdentifier.Parse("\"@authority\""),
            MessageComponentIdentifier.Parse("\"content-digest\""),
            MessageComponentIdentifier.Parse("\"@query-param\";name=\"Pet\""),
        };

        var parameters = new SignatureParameters { Created = 1618884473, KeyId = "test-key-rsa-pss", Tag = "header-example" };
        var signatureBase = SignatureBase.Create(TestRequest(), components, SignatureParameters.SerializeValue(components, parameters));

        Assert.Equal(
            "\"@authority\": example.com\n"
          + $"\"content-digest\": {ContentDigest}\n"
          + "\"@query-param\";name=\"Pet\": dog\n"
          + "\"@signature-params\": (\"@authority\" \"content-digest\" \"@query-param\";name=\"Pet\");created=1618884473;keyid=\"test-key-rsa-pss\";tag=\"header-example\"",
            signatureBase
        );
    }

    [Fact]
    public void Build_The_Appendix_B23_Full_Coverage_Base() {
        var components = new List<MessageComponentIdentifier> {
            MessageComponentIdentifier.Parse("\"date\""),
            MessageComponentIdentifier.Parse("\"@method\""),
            MessageComponentIdentifier.Parse("\"@path\""),
            MessageComponentIdentifier.Parse("\"@query\""),
            MessageComponentIdentifier.Parse("\"@authority\""),
            MessageComponentIdentifier.Parse("\"content-type\""),
            MessageComponentIdentifier.Parse("\"content-digest\""),
            MessageComponentIdentifier.Parse("\"content-length\""),
        };

        var parameters = new SignatureParameters { Created = 1618884473, KeyId = "test-key-rsa-pss" };
        var signatureBase = SignatureBase.Create(TestRequest(), components, SignatureParameters.SerializeValue(components, parameters));

        Assert.Equal(
            "\"date\": Tue, 20 Apr 2021 02:07:55 GMT\n"
          + "\"@method\": POST\n"
          + "\"@path\": /foo\n"
          + "\"@query\": ?param=Value&Pet=dog\n"
          + "\"@authority\": example.com\n"
          + "\"content-type\": application/json\n"
          + $"\"content-digest\": {ContentDigest}\n"
          + "\"content-length\": 18\n"
          + "\"@signature-params\": (\"date\" \"@method\" \"@path\" \"@query\" \"@authority\" \"content-type\" \"content-digest\" \"content-length\");created=1618884473;keyid=\"test-key-rsa-pss\"",
            signatureBase
        );
    }

    [Fact]
    public void Combine_Repeated_Field_Instances_With_A_Single_Comma_And_Space() {
        var message = Request(fields: [
            new("cache-control", "max-age=60"),
            new("cache-control", "must-revalidate"),
        ]);

        var signatureBase = SignatureBase.Create(message, [MessageComponentIdentifier.Parse("cache-control")], "()");

        Assert.StartsWith("\"cache-control\": max-age=60, must-revalidate\n", signatureBase);
    }

    [Fact]
    public void Wrap_Binary_Flagged_Field_Instances_As_Byte_Sequences() {
        var message = Request(fields: [
            new("example-header", "value, with, lots"),
            new("example-header", "of, commas"),
        ]);

        var signatureBase = SignatureBase.Create(message, [MessageComponentIdentifier.Parse("\"example-header\";bs")], "()");

        Assert.StartsWith("\"example-header\";bs: :dmFsdWUsIHdpdGgsIGxvdHM=:, :b2YsIGNvbW1hcw==:\n", signatureBase);
    }

    [Fact]
    public void Encode_Named_Query_Parameters_After_Form_Decoding() {
        var message = Request(query: "?var=this%20is%20a%20big%0Amultiline%20value&bar=with+plus+whitespace");

        var signatureBase = SignatureBase.Create(
            message,
            [
                MessageComponentIdentifier.Parse("\"@query-param\";name=\"var\""),
                MessageComponentIdentifier.Parse("\"@query-param\";name=\"bar\""),
            ],
            "()"
        );

        Assert.StartsWith(
            "\"@query-param\";name=\"var\": this%20is%20a%20big%0Amultiline%20value\n\"@query-param\";name=\"bar\": with%20plus%20whitespace\n",
            signatureBase
        );
    }

    [Fact]
    public void Reject_A_Duplicate_Component_Identifier() {
        var components = new List<MessageComponentIdentifier> {
            MessageComponentIdentifier.Parse("@method"),
            MessageComponentIdentifier.Parse("@method"),
        };

        Assert.Throws<InvalidArgumentException>(() => SignatureBase.Create(TestRequest(), components, "()"));
    }

    [Fact]
    public void Reject_The_Signature_Params_Component_In_The_Covered_Set() {
        var components = new List<MessageComponentIdentifier> {
            MessageComponentIdentifier.Parse("@signature-params"),
        };

        Assert.Throws<InvalidArgumentException>(() => SignatureBase.Create(TestRequest(), components, "()"));
    }

    [Fact]
    public void Reject_An_Unknown_Derived_Component() {
        Assert.Throws<InvalidArgumentException>(
            () => SignatureBase.Create(TestRequest(), [MessageComponentIdentifier.Parse("@unknown")], "()")
        );
    }

    [Fact]
    public void Reject_A_Field_Absent_From_The_Message() {
        Assert.Throws<InvalidArgumentException>(
            () => SignatureBase.Create(TestRequest(), [MessageComponentIdentifier.Parse("x-absent")], "()")
        );
    }

    [Fact]
    public void Reject_A_Query_Parameter_Absent_From_The_Query() {
        Assert.Throws<InvalidArgumentException>(
            () => SignatureBase.Create(TestRequest(), [MessageComponentIdentifier.Parse("\"@query-param\";name=\"absent\"")], "()")
        );
    }

    [Fact]
    public void Reject_A_Query_Parameter_Occurring_More_Than_Once() {
        var message = Request(query: "?pet=dog&pet=cat");

        Assert.Throws<InvalidArgumentException>(
            () => SignatureBase.Create(message, [MessageComponentIdentifier.Parse("\"@query-param\";name=\"pet\"")], "()")
        );
    }

    [Fact]
    public void Reject_The_Req_Flag_On_A_Request_Target() {
        Assert.Throws<InvalidArgumentException>(
            () => SignatureBase.Create(TestRequest(), [MessageComponentIdentifier.Parse("\"@method\";req")], "()")
        );
    }

    [Fact]
    public void Reject_A_Non_Ascii_Component_Value() {
        var message = Request(fields: [new("x-greeting", "héllo")]);

        Assert.Throws<InvalidArgumentException>(
            () => SignatureBase.Create(message, [MessageComponentIdentifier.Parse("x-greeting")], "()")
        );
    }

    private static SignatureMessage TestRequest() {
        return Request(
            "?param=Value&Pet=dog",
            [
                new("date", "Tue, 20 Apr 2021 02:07:55 GMT"),
                new("content-type", "application/json"),
                new("content-digest", ContentDigest),
                new("content-length", "18"),
            ]
        );
    }

    private static SignatureMessage Request(string? query = null, IReadOnlyList<SignatureField>? fields = null) {
        return new(
            method: "POST",
            scheme: "https",
            authority: "example.com",
            path: "/foo",
            query: query,
            fields: fields ?? []
        );
    }
}
