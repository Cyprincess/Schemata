using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Security.Foundation.Signatures;
using Schemata.Security.Skeleton.Services;
using Xunit;

namespace Schemata.Security.Tests;

public class HttpMessageSignerShould
{
    // RFC 9421 appendix B.1.5: the test-shared-secret, base64-encoded.
    private const string SharedSecret =
        "uzvJfB4u3N0Jy4T7NZ75MDVcr8zSTInedJtkgcu46YW4XByzNJjxBdtjUkdJPBtbmHhIDi6pcl8jsasjlTMtDQ==";

    // RFC 9421 appendix B.1.3: the test-key-ecc-p256 public key.
    private const string EccP256PublicKey = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEqIVYZVLCrPZHGHjP17CTW0/+D9Lf
        w0EkjqF7xB4FivAxzic30tMM4GF+hR6Dxh71Z50VGGdldkkDXZCnTNnoXQ==
        -----END PUBLIC KEY-----
        """;

    [Fact]
    public void Sign_The_Appendix_B25_Hmac_Known_Answer() {
        var signer = new HttpMessageSigner(Clock(1618884473).Object);
        var signature = signer.Sign(
            TestRequest(),
            Components("date", "@authority", "content-type"),
            new SecurityKeyMaterial.Symmetric(Convert.FromBase64String(SharedSecret)),
            new() { Label = "sig-b25", KeyId = "test-shared-secret" }
        );

        Assert.Equal("(\"date\" \"@authority\" \"content-type\");created=1618884473;keyid=\"test-shared-secret\"", signature.Input);
        Assert.Equal(":pxcQw6G3AjtMBQjwo8XzkZf/bws5LelbaMk5rGIGtE8=:", signature.Value);
    }

    [Fact]
    public void Verify_The_Appendix_B24_Ecdsa_Known_Answer() {
        var message = TestResponse(fields: [
            new("signature-input", "sig-b24=(\"@status\" \"content-type\" \"content-digest\" \"content-length\");created=1618884473;keyid=\"test-key-ecc-p256\""),
            new("signature", "sig-b24=:wNmSUAhwb5LxtOtOpNa6W5xj067m5hFrj0XQ4fvpaCLx0NKocgPquLgyahnzDnDAUy5eCdlYUEkLIj+32oiasw==:"),
        ]);

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(EccP256PublicKey);

        var verifier = new HttpMessageSignatureVerifier(Clock(1618884473).Object);
        Assert.True(verifier.Verify(message, new SecurityKeyMaterial.EcKey(ecdsa)));
    }

    [Fact]
    public void Reject_The_Appendix_B24_Signature_Against_A_Tampered_Field() {
        var message = TestResponse(
            contentLength: "24",
            fields: [
                new("signature-input", "sig-b24=(\"@status\" \"content-type\" \"content-digest\" \"content-length\");created=1618884473;keyid=\"test-key-ecc-p256\""),
                new("signature", "sig-b24=:wNmSUAhwb5LxtOtOpNa6W5xj067m5hFrj0XQ4fvpaCLx0NKocgPquLgyahnzDnDAUy5eCdlYUEkLIj+32oiasw==:"),
            ]
        );

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(EccP256PublicKey);

        var verifier = new HttpMessageSignatureVerifier(Clock(1618884473).Object);
        Assert.False(verifier.Verify(message, new SecurityKeyMaterial.EcKey(ecdsa)));
    }

    [Fact]
    public void Sign_And_Verify_A_Round_Trip_With_Rsa_Pss() {
        using var rsa = RSA.Create(2048);
        var key     = new SecurityKeyMaterial.RsaKey(rsa);
        var signer  = new HttpMessageSigner(Clock(1618884473).Object);
        var message = TestRequest();

        var signature = signer.Sign(
            message,
            Components("@method", "@authority", "@path", "content-digest", "content-type", "content-length"),
            key,
            new() { KeyId = "test-key-rsa-pss" }
        );

        var signed = TestRequest(fields: [
            new("signature-input", signature.InputMember),
            new("signature", signature.ValueMember),
        ]);

        var verifier = new HttpMessageSignatureVerifier(Clock(1618884473).Object);
        Assert.True(verifier.Verify(signed, key));
    }

    [Fact]
    public void Reject_A_Tampered_Covered_Component_And_A_Tampered_Body_Digest() {
        var key    = new SecurityKeyMaterial.Symmetric(Convert.FromBase64String(SharedSecret));
        var signer = new HttpMessageSigner(Clock(1618884473).Object);
        var signature = signer.Sign(
            TestRequest(),
            Components("@method", "content-digest"),
            key,
            new() { KeyId = "test-shared-secret" }
        );

        var verifier = new HttpMessageSignatureVerifier(Clock(1618884473).Object);

        var tamperedMethod = TestRequest(
            method: "PUT",
            fields: [new("signature-input", signature.InputMember), new("signature", signature.ValueMember)]
        );
        Assert.False(verifier.Verify(tamperedMethod, key));

        var tamperedDigest = TestRequest(
            digest: "sha-512=:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==:",
            fields: [new("signature-input", signature.InputMember), new("signature", signature.ValueMember)]
        );
        Assert.False(verifier.Verify(tamperedDigest, key));
    }

    [Fact]
    public void Reject_A_Signature_Older_Than_The_Configured_Maximum_Age() {
        var key = new SecurityKeyMaterial.Symmetric(Convert.FromBase64String(SharedSecret));
        var signature = new HttpMessageSigner(Clock(1618884473).Object).Sign(
            TestRequest(),
            Components("@method"),
            key
        );

        var fields = new[] { new SignatureField("signature-input", signature.InputMember), new SignatureField("signature", signature.ValueMember) };
        var options = new HttpMessageSignatureVerificationOptions { MaximumSignatureAge = TimeSpan.FromMinutes(5) };

        var inside = new HttpMessageSignatureVerifier(Clock(1618884473 + 299).Object);
        Assert.True(inside.Verify(TestRequest(fields: fields), key, options));

        var outside = new HttpMessageSignatureVerifier(Clock(1618884473 + 301).Object);
        Assert.False(outside.Verify(TestRequest(fields: fields), key, options));
    }

    [Fact]
    public void Reject_A_Signature_Past_Its_Expiration() {
        var key = new SecurityKeyMaterial.Symmetric(Convert.FromBase64String(SharedSecret));
        var signature = new HttpMessageSigner(Clock(1618884473).Object).Sign(
            TestRequest(),
            Components("@method"),
            key,
            new() { ExpiresAfter = TimeSpan.FromMinutes(5) }
        );

        var fields = new[] { new SignatureField("signature-input", signature.InputMember), new SignatureField("signature", signature.ValueMember) };

        var before = new HttpMessageSignatureVerifier(Clock(1618884473 + 299).Object);
        Assert.True(before.Verify(TestRequest(fields: fields), key));

        var after = new HttpMessageSignatureVerifier(Clock(1618884773 + 1).Object);
        Assert.False(after.Verify(TestRequest(fields: fields), key));
    }

    [Fact]
    public void Reject_A_Signature_Missing_A_Required_Covered_Component() {
        var key = new SecurityKeyMaterial.Symmetric(Convert.FromBase64String(SharedSecret));
        var signature = new HttpMessageSigner(Clock(1618884473).Object).Sign(
            TestRequest(),
            Components("@method"),
            key
        );

        var options = new HttpMessageSignatureVerificationOptions();
        options.RequiredComponents.Add("content-digest");

        var verifier = new HttpMessageSignatureVerifier(Clock(1618884473).Object);
        Assert.False(
            verifier.Verify(
                TestRequest(fields: [new("signature-input", signature.InputMember), new("signature", signature.ValueMember)]),
                key,
                options
            )
        );
    }

    [Fact]
    public void Resolve_The_Verification_Key_By_The_Keyid_Parameter() {
        var key = new SecurityKeyMaterial.Symmetric(Convert.FromBase64String(SharedSecret));
        var signature = new HttpMessageSigner(Clock(1618884473).Object).Sign(
            TestRequest(),
            Components("@method"),
            key,
            new() { KeyId = "test-shared-secret" }
        );

        var message = TestRequest(fields: [new("signature-input", signature.InputMember), new("signature", signature.ValueMember)]);
        var verifier = new HttpMessageSignatureVerifier(Clock(1618884473).Object);

        string? resolved = null;
        Assert.True(verifier.Verify(message, keyid => {
            resolved = keyid;
            return key;
        }));
        Assert.Equal("test-shared-secret", resolved);

        Assert.False(verifier.Verify(message, _ => null));
    }

    [Fact]
    public void Sign_The_Components_Declared_By_The_Surface_Attribute() {
        var attribute = new HttpMessageSignatureAttribute("@method", "@authority", "content-digest") { KeyId = "test-shared-secret" };

        var key       = new SecurityKeyMaterial.Symmetric(Convert.FromBase64String(SharedSecret));
        var signature = new HttpMessageSigner(Clock(1618884473).Object).Sign(TestRequest(), attribute.GetComponents(), key, new() { KeyId = attribute.KeyId });

        Assert.Equal(
            "(\"@method\" \"@authority\" \"content-digest\");created=1618884473;keyid=\"test-shared-secret\"",
            signature.Input
        );
    }

    [Fact]
    public void Reject_Key_Material_Inappropriate_For_The_Explicit_Algorithm() {
        var signer = new HttpMessageSigner(Clock(1618884473).Object);

        Assert.Throws<InvalidArgumentException>(
            () => signer.Sign(
                TestRequest(),
                Components("@method"),
                new SecurityKeyMaterial.Symmetric(Convert.FromBase64String(SharedSecret)),
                new() { Algorithm = SignatureConstants.Algorithms.EcdsaP256Sha256 }
            )
        );
    }

    [Fact]
    public void Return_False_Instead_Of_Throwing_On_An_Empty_Signature_Value_With_Hmac() {
        var key       = new SecurityKeyMaterial.Symmetric(Convert.FromBase64String(SharedSecret));
        var signer    = new HttpMessageSigner(Clock(1618884473).Object);
        var signature = signer.Sign(TestRequest(), Components("date", "@authority", "content-type"), key, new() { KeyId = "test-shared-secret" });

        var tampered = TestRequest(fields: [
            new("signature-input", signature.InputMember),
            new("signature", "sig=::"),
        ]);

        var verifier = new HttpMessageSignatureVerifier(Clock(1618884473).Object);
        Assert.False(verifier.Verify(tampered, key));
    }

    [Fact]
    public void Return_False_Instead_Of_Throwing_On_A_Truncated_Signature_Value_With_Rsa() {
        using var rsa = RSA.Create(2048);
        var key       = new SecurityKeyMaterial.RsaKey(rsa);
        var signer    = new HttpMessageSigner(Clock(1618884473).Object);
        var signature = signer.Sign(TestRequest(), Components("@method", "@authority", "content-type"), key, new() { KeyId = "test-key-rsa-pss" });

        var verifier = new HttpMessageSignatureVerifier(Clock(1618884473).Object);
        foreach (var presented in new[] { "sig=::", "sig=:YQ==:" }) {
            var tampered = TestRequest(fields: [
                new("signature-input", signature.InputMember),
                new("signature", presented),
            ]);

            Assert.False(verifier.Verify(tampered, key));
        }
    }

    [Fact]
    public void Return_False_Instead_Of_Throwing_On_A_Truncated_Signature_Value_With_Ecdsa() {
        using var ec  = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var key       = new SecurityKeyMaterial.EcKey(ec);
        var signer    = new HttpMessageSigner(Clock(1618884473).Object);
        var signature = signer.Sign(TestRequest(), Components("@method", "@authority", "content-type"), key, new() { KeyId = "test-key-ecc-p256" });

        var verifier = new HttpMessageSignatureVerifier(Clock(1618884473).Object);
        foreach (var presented in new[] { "sig=::", "sig=:YQ==:" }) {
            var tampered = TestRequest(fields: [
                new("signature-input", signature.InputMember),
                new("signature", presented),
            ]);

            Assert.False(verifier.Verify(tampered, key));
        }
    }

    [Fact]
    public void Reject_A_Signature_Input_Carrying_An_Unquoted_Member() {
        var key       = new SecurityKeyMaterial.Symmetric(Convert.FromBase64String(SharedSecret));
        var signer    = new HttpMessageSigner(Clock(1618884473).Object);
        var signature = signer.Sign(TestRequest(), Components("content-type"), key, new() { KeyId = "test-shared-secret" });

        var forged = TestRequest(fields: [
            new("signature-input", "sig=(@method \"content-type\");created=1618884473;keyid=\"test-shared-secret\""),
            new("signature", signature.ValueMember),
        ]);

        var verifier = new HttpMessageSignatureVerifier(Clock(1618884473).Object);
        Assert.False(verifier.Verify(forged, key));
    }

    [Fact]
    public void Reject_The_Req_Flag_On_The_Status_Component() {
        var key      = new SecurityKeyMaterial.Symmetric(Convert.FromBase64String(SharedSecret));
        var signer   = new HttpMessageSigner(Clock(1618884473).Object);
        var response = new SignatureMessage(status: 200, fields: TestResponse().Fields, request: TestRequest());

        Assert.Throws<InvalidArgumentException>(() => signer.Sign(response, Components("\"@status\";req"), key));
    }

    [Theory]
    [InlineData("Sig")]
    [InlineData("1sig")]
    [InlineData("sig!")]
    [InlineData("")]
    public void Reject_A_Label_That_Is_Not_An_Sf_Key(string label) {
        var key    = new SecurityKeyMaterial.Symmetric(Convert.FromBase64String(SharedSecret));
        var signer = new HttpMessageSigner(Clock(1618884473).Object);

        Assert.Throws<InvalidArgumentException>(() => signer.Sign(TestRequest(), Components("@method"), key, new() { Label = label }));
    }

    [Fact]
    public void Accept_A_Label_With_Sf_Key_Punctuation() {
        var key       = new SecurityKeyMaterial.Symmetric(Convert.FromBase64String(SharedSecret));
        var signer    = new HttpMessageSigner(Clock(1618884473).Object);
        var signature = signer.Sign(TestRequest(), Components("@method"), key, new() { Label = "sig.1-*" });

        Assert.StartsWith("sig.1-*=", signature.InputMember);
    }

    private static Mock<TimeProvider> Clock(long unixSeconds) {
        var clock = new Mock<TimeProvider>();
        clock.Setup(provider => provider.GetUtcNow()).Returns(DateTimeOffset.FromUnixTimeSeconds(unixSeconds));
        return clock;
    }

    private static List<MessageComponentIdentifier> Components(params string[] components) {
        var parsed = new List<MessageComponentIdentifier>(components.Length);
        foreach (var component in components) {
            parsed.Add(MessageComponentIdentifier.Parse(component));
        }

        return parsed;
    }

    private static SignatureMessage TestRequest(string method = "POST", string? digest = null, IReadOnlyList<SignatureField>? fields = null) {
        var all = new List<SignatureField> {
            new("date", "Tue, 20 Apr 2021 02:07:55 GMT"),
            new("content-type", "application/json"),
            new("content-digest", digest ?? "sha-512=:WZDPaVn/7XgHaAy8pmojAkGWoRx2UFChF41A2svX+TaPm+AbwAgBWnrIiYllu7BNNyealdVLvRwEmTHWXvJwew==:"),
            new("content-length", "18"),
        };
        if (fields is not null) {
            all.AddRange(fields);
        }

        return new(
            method: method,
            scheme: "https",
            authority: "example.com",
            path: "/foo",
            query: "?param=Value&Pet=dog",
            fields: all
        );
    }

    private static SignatureMessage TestResponse(string contentLength = "23", IReadOnlyList<SignatureField>? fields = null) {
        var all = new List<SignatureField> {
            new("content-type", "application/json"),
            new("content-digest", "sha-512=:mEWXIS7MaLRuGgxOBdODa3xqM1XdEvxoYhvlCFJ41QJgJc4GTsPp29l5oGX69wWdXymyU0rjJuahq4l5aGgfLQ==:"),
            new("content-length", contentLength),
        };
        if (fields is not null) {
            all.AddRange(fields);
        }

        return new(status: 200, fields: all);
    }
}
