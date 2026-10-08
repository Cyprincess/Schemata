using System;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using Schemata.Push.Foundation.WebPush;
using Xunit;

namespace Schemata.Push.Tests;

[Trait("Layer", "Unit")]
public class WebPushEncryptionShould
{
    // RFC 8291 appendix A intermediate values for the section 5 example message.
    private const string Plaintext      = "V2hlbiBJIGdyb3cgdXAsIEkgd2FudCB0byBiZSBhIHdhdGVybWVsb24";
    private const string SenderPrivate  = "yfWPiYE-n46HLnH0KqZOF1fJJU3MYrct3AELtAQ-oRw";
    private const string SenderPublic   = "BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8";
    private const string ReceiverPublic = "BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4";
    private const string Salt           = "DGv6ra1nlYgDCS1FRnbzlw";
    private const string AuthSecret     = "BTBZMqHH6r4Tts7J_aSIgg";

    private const string ExpectedBody =
        "DGv6ra1nlYgDCS1FRnbzlwAAEABBBP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A_yl95bQpu6cVPTpK4Mqgkf1CXztLVBSt2Ks3oZwbuwXPXLWyouBWLVWGNWQexSgSxsj_Qulcy4a-fN";

    [Fact]
    public void Match_The_Rfc8291_Appendix_A_Known_Answer() {
        var senderPublic = Base64UrlEncoder.DecodeBytes(SenderPublic);
        using var sender = ECDiffieHellman.Create(new ECParameters {
            Curve = ECCurve.NamedCurves.nistP256,
            D     = Base64UrlEncoder.DecodeBytes(SenderPrivate),
            Q = new() {
                X = senderPublic[1..33],
                Y = senderPublic[33..],
            },
        });

        var body = WebPushEncryption.Encrypt(
            Base64UrlEncoder.DecodeBytes(Plaintext),
            Base64UrlEncoder.DecodeBytes(ReceiverPublic),
            Base64UrlEncoder.DecodeBytes(AuthSecret),
            sender,
            Base64UrlEncoder.DecodeBytes(Salt));

        Assert.Equal(ExpectedBody, Base64UrlEncoder.Encode(body));
    }

    [Fact]
    public void Reject_Payloads_Beyond_The_Single_Record_Limit() {
        Assert.Throws<ArgumentOutOfRangeException>(() => WebPushEncryption.Encrypt(
            new byte[WebPushEncryption.MaxPayloadLength + 1],
            Base64UrlEncoder.DecodeBytes(ReceiverPublic),
            Base64UrlEncoder.DecodeBytes(AuthSecret)));
    }

    [Fact]
    public void Reject_Subscription_Keys_That_Are_Not_Uncompressed_P256_Points() {
        Assert.Throws<ArgumentException>(() => WebPushEncryption.Encrypt(
            [1],
            new byte[64],
            Base64UrlEncoder.DecodeBytes(AuthSecret)));
    }
}
