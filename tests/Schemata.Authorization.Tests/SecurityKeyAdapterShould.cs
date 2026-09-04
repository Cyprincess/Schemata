using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Schemata.Authorization.Foundation.Services;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;

namespace Schemata.Authorization.Tests;

public class SecurityKeyAdapterShould
{
    [Fact]
    public void Adapt_An_Rsa_Key_And_Stamp_The_Row_Kid() {
        using var rsa = RSA.Create(2048);
        var material = Material(new SecurityKeyMaterial.RsaKey(rsa), "k1");

        var key = Assert.IsType<RsaSecurityKey>(SecurityKeyAdapter.ToSecurityKey(material));

        Assert.Equal("k1", key.KeyId);
        Assert.Equal(2048, key.KeySize);
    }

    [Fact]
    public void Adapt_An_Ec_Key_And_Stamp_The_Row_Kid() {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var material = Material(new SecurityKeyMaterial.EcKey(ec), "k2");

        var key = Assert.IsType<ECDsaSecurityKey>(SecurityKeyAdapter.ToSecurityKey(material));

        Assert.Equal("k2", key.KeyId);
        Assert.Equal(256, key.KeySize);
    }

    [Fact]
    public void Adapt_A_Symmetric_Key_And_Stamp_The_Row_Kid() {
        var material = Material(
            new SecurityKeyMaterial.Symmetric(Encoding.UTF8.GetBytes("shared-secret")), "k3");

        var key = Assert.IsType<SymmetricSecurityKey>(SecurityKeyAdapter.ToSecurityKey(material));

        Assert.Equal("k3", key.KeyId);
        Assert.Equal(Encoding.UTF8.GetBytes("shared-secret"), key.Key);
    }

    [Fact]
    public void Adapt_An_Rsa_Public_Pem_And_Stamp_The_Row_Kid() {
        using var rsa = RSA.Create(2048);
        var material = Material(
            new SecurityKeyMaterial.PublicKeyPem(rsa.ExportSubjectPublicKeyInfoPem()), "k4");

        var key = Assert.IsType<RsaSecurityKey>(SecurityKeyAdapter.ToSecurityKey(material));

        Assert.Equal("k4", key.KeyId);
        Assert.Equal(2048, key.KeySize);
    }

    [Fact]
    public void Adapt_An_Ec_Public_Pem_And_Stamp_The_Row_Kid() {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var material = Material(
            new SecurityKeyMaterial.PublicKeyPem(ec.ExportSubjectPublicKeyInfoPem()), "k5");

        var key = Assert.IsType<ECDsaSecurityKey>(SecurityKeyAdapter.ToSecurityKey(material));

        Assert.Equal("k5", key.KeyId);
        Assert.Equal(256, key.KeySize);
    }

    [Fact]
    public void Return_Null_For_Json_Materials_Because_They_Publish_Through_Key_Sets() {
        var jwk  = Material(new SecurityKeyMaterial.JwkJson(JsonWebKeyJson("jwk-1")));
        var jwks = Material(new SecurityKeyMaterial.JwksJson(JsonWebSetJson("jwk-2")));

        Assert.Null(SecurityKeyAdapter.ToSecurityKey(jwk));
        Assert.Null(SecurityKeyAdapter.ToSecurityKey(jwks));
    }

    [Fact]
    public void Throw_For_Uri_Materials_Until_They_Are_Resolved() {
        var uri   = Material(new SecurityKeyMaterial.KeyUri("https://issuer.example.com/jwks"));
        var found = new[] { uri };

        Assert.Throws<InvalidOperationException>(() => SecurityKeyAdapter.ToSecurityKey(uri));
        Assert.Throws<InvalidOperationException>(() => SecurityKeyAdapter.ToJsonWebKeySet(found));
    }

    [Fact]
    public void Parse_A_Single_Jwk_Keeping_Its_Own_Kid() {
        var material = Material(new SecurityKeyMaterial.JwkJson(JsonWebKeyJson("jwk-1")), "row-kid");

        var set = SecurityKeyAdapter.ToJsonWebKeySet(new[] { material });

        var key = Assert.Single(set.Keys);
        Assert.Equal("jwk-1", key.Kid);
    }

    [Fact]
    public void Parse_A_Json_Key_Set_Keeping_Its_Own_Kids() {
        var material = Material(new SecurityKeyMaterial.JwksJson(JsonWebSetJson("jwk-a", "jwk-b")), "row-kid");

        var set = SecurityKeyAdapter.ToJsonWebKeySet(new[] { material });

        Assert.Equal(2, set.Keys.Count);
        Assert.Equal(
            new[] { "jwk-a", "jwk-b" }.OrderBy(k => k),
            set.Keys.Select(k => k.Kid).OrderBy(k => k));
    }

    [Fact]
    public void Combine_Mixed_Materials_Into_One_Key_Set() {
        using var rsa = RSA.Create(2048);
        using var ec  = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var materials = new List<SchemataKeyMaterial> {
            Material(new SecurityKeyMaterial.RsaKey(rsa), "rsa-row"),
            Material(new SecurityKeyMaterial.EcKey(ec), "ec-row"),
            Material(new SecurityKeyMaterial.Symmetric(Encoding.UTF8.GetBytes("shared")), "hmac-row"),
            Material(new SecurityKeyMaterial.JwkJson(JsonWebKeyJson("jwk-row")), "row-kid"),
            Material(new SecurityKeyMaterial.JwksJson(JsonWebSetJson("jwk-a", "jwk-b")), "row-kid"),
        };

        var set = SecurityKeyAdapter.ToJsonWebKeySet(materials);

        Assert.Equal(6, set.Keys.Count);
        Assert.Equal(
            new[] { "rsa-row", "ec-row", "hmac-row", "jwk-row", "jwk-a", "jwk-b" }.OrderBy(k => k),
            set.Keys.Select(k => k.Kid).OrderBy(k => k));
    }

    private static SchemataKeyMaterial Material(SecurityKeyMaterial material, string? kid = null) {
        return new(new() { Kid = kid }, material);
    }

    private static string JsonWebKeyJson(string kid) {
        return $$"""{"kty":"RSA","kid":"{{kid}}","n":"0vx7agoebGcQS0Pi","e":"AQAB"}""";
    }

    private static string JsonWebSetJson(params string[] kids) {
        var keys = string.Join(
            ",", Array.ConvertAll(kids, kid => JsonWebKeyJson(kid)));
        return $$"""{"keys":[{{keys}}]}""";
    }
}
