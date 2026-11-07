using Schemata.Scim;
using Schemata.Scim.Models;

namespace Schemata.Authorization.Scim.Services;

/// <summary>Declares the project-owned Application and authorization-extension SCIM schemas.</summary>
public static class ScimApplicationSchema
{
    public const string ApplicationUri = "https://github.com/Cyprincess/Schemata/scim/schemas/2.0/Application";
    public const string AuthorizationUri = "https://github.com/Cyprincess/Schemata/scim/schemas/2.0/ApplicationAuthorization";

    public static readonly ScimResourceType ResourceType = new() {
        Id = "Application",
        Name = "Application",
        Endpoint = "/Applications",
        Schema = ApplicationUri,
        Description = "OAuth and OpenID Connect application",
        SchemaExtensions = [new() { Schema = AuthorizationUri, Required = false }],
    };

    public static readonly ScimSchema Application = new() {
        Id = ApplicationUri,
        Name = "Application",
        Description = "Public OAuth and OpenID Connect registration metadata",
        Attributes = [
            .. ScimSchemaRegistry.CommonAttributes(),
            String("clientId", ScimConstants.ReadOnly),
            Integer("clientIdIssuedAt", ScimConstants.ReadOnly),
            Strings("redirectUris"),
            String("tokenEndpointAuthMethod"),
            Strings("grantTypes"),
            Strings("responseTypes"),
            String("clientName"),
            String("clientUri"),
            String("logoUri"),
            String("scope"),
            Strings("contacts"),
            String("tosUri"),
            String("policyUri"),
            String("jwksUri"),
            new() {
                Name = "jwks",
                Type = ScimConstants.ComplexType,
                MultiValued = true,
                Mutability = ScimConstants.ReadWrite,
                SubAttributes = [
                    String("kty"), String("kid"), String("use"), String("alg"),
                    String("n"), String("e"), String("crv"), String("x"), String("y"),
                    Strings("key_ops"), Strings("x5c"),
                    Secret("d"), Secret("k"), Secret("p"), Secret("q"), Secret("dp"), Secret("dq"), Secret("qi"), Secret("oth"),
                ],
            },
            String("applicationType"),
            String("sectorIdentifierUri"),
            String("subjectType"),
            String("idTokenSignedResponseAlg"),
            String("idTokenEncryptedResponseAlg"),
            String("idTokenEncryptedResponseEnc"),
            String("userinfoSignedResponseAlg"),
            String("userinfoEncryptedResponseAlg"),
            String("userinfoEncryptedResponseEnc"),
            String("tokenEndpointAuthSigningAlg"),
            Integer("defaultMaxAge"),
            Boolean("requireAuthTime"),
            Strings("defaultAcrValues"),
            String("initiateLoginUri"),
            Strings("postLogoutRedirectUris"),
            String("frontchannelLogoutUri"),
            Boolean("frontchannelLogoutSessionRequired"),
            String("backchannelLogoutUri"),
            Boolean("backchannelLogoutSessionRequired"),
            String("softwareId"),
            String("softwareVersion"),
            String("softwareStatement"),
            Boolean("requirePushedAuthorizationRequests"),
            String("requestObjectSigningAlg"),
            String("requestObjectEncryptionAlg"),
            String("requestObjectEncryptionEnc"),
            Strings("requestUris"),
            Boolean("requireSignedRequestObject"),
            Boolean("dpopBoundAccessTokens"),
            Strings("authorizationDetailsTypes"),
        ],
    };

    public static readonly ScimSchema Authorization = new() {
        Id = AuthorizationUri,
        Name = "ApplicationAuthorization",
        Description = "Administrator-delegated Application permissions",
        Attributes = [Strings("permissions")],
    };

    private static ScimAttribute String(string name, string? mutability = null) {
        return new() { Name = name, Type = ScimConstants.StringType, Mutability = mutability ?? ScimConstants.ReadWrite };
    }

    private static ScimAttribute Strings(string name) {
        return new() { Name = name, Type = ScimConstants.StringType, MultiValued = true, Mutability = ScimConstants.ReadWrite };
    }

    private static ScimAttribute Integer(string name, string? mutability = null) {
        return new() { Name = name, Type = ScimConstants.IntegerType, Mutability = mutability ?? ScimConstants.ReadWrite };
    }

    private static ScimAttribute Boolean(string name) {
        return new() { Name = name, Type = ScimConstants.BooleanType, Mutability = ScimConstants.ReadWrite };
    }

    private static ScimAttribute Secret(string name) {
        return new() {
            Name = name,
            Type = ScimConstants.StringType,
            Mutability = ScimConstants.WriteOnly,
            Returned = ScimConstants.Never,
        };
    }
}
