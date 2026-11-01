namespace Schemata.Scim;

/// <summary>
///     Wire constants defined by RFC 7643 and RFC 7644.
/// </summary>
public static class ScimConstants
{
    /// <summary>The default SCIM base path.</summary>
    public const string BasePath = "/scim/v2";

    /// <summary>The SCIM JSON media type.</summary>
    public const string MediaType = "application/scim+json";

    /// <summary>The ListResponse message schema URI.</summary>
    public const string ListResponseSchema = "urn:ietf:params:scim:api:messages:2.0:ListResponse";

    /// <summary>The Error message schema URI.</summary>
    public const string ErrorSchema = "urn:ietf:params:scim:api:messages:2.0:Error";

    /// <summary>The ServiceProviderConfig schema URI.</summary>
    public const string ServiceProviderConfigSchema = "urn:ietf:params:scim:schemas:core:2.0:ServiceProviderConfig";

    /// <summary>The ResourceType schema URI.</summary>
    public const string ResourceTypeSchema = "urn:ietf:params:scim:schemas:core:2.0:ResourceType";

    /// <summary>The Schema schema URI.</summary>
    public const string SchemaSchema = "urn:ietf:params:scim:schemas:core:2.0:Schema";

    /// <summary>The User schema URI.</summary>
    public const string UserSchema = "urn:ietf:params:scim:schemas:core:2.0:User";

    /// <summary>The Group schema URI.</summary>
    public const string GroupSchema = "urn:ietf:params:scim:schemas:core:2.0:Group";

    /// <summary>The Enterprise User extension schema URI.</summary>
    public const string EnterpriseUserSchema = "urn:ietf:params:scim:schemas:extension:enterprise:2.0:User";

    /// <summary>The <c>string</c> attribute type.</summary>
    public const string StringType = "string";

    /// <summary>The <c>boolean</c> attribute type.</summary>
    public const string BooleanType = "boolean";

    /// <summary>The <c>decimal</c> attribute type.</summary>
    public const string DecimalType = "decimal";

    /// <summary>The <c>integer</c> attribute type.</summary>
    public const string IntegerType = "integer";

    /// <summary>The <c>dateTime</c> attribute type.</summary>
    public const string DateTimeType = "dateTime";

    /// <summary>The <c>binary</c> attribute type.</summary>
    public const string BinaryType = "binary";

    /// <summary>The <c>reference</c> attribute type.</summary>
    public const string ReferenceType = "reference";

    /// <summary>The <c>complex</c> attribute type.</summary>
    public const string ComplexType = "complex";

    /// <summary>The <c>readOnly</c> mutability.</summary>
    public const string ReadOnly = "readOnly";

    /// <summary>The <c>readWrite</c> mutability.</summary>
    public const string ReadWrite = "readWrite";

    /// <summary>The <c>immutable</c> mutability.</summary>
    public const string Immutable = "immutable";

    /// <summary>The <c>writeOnly</c> mutability.</summary>
    public const string WriteOnly = "writeOnly";

    /// <summary>The <c>always</c> returned characteristic.</summary>
    public const string Always = "always";

    /// <summary>The <c>never</c> returned characteristic.</summary>
    public const string Never = "never";

    /// <summary>The <c>default</c> returned characteristic.</summary>
    public const string Default = "default";

    /// <summary>The <c>request</c> returned characteristic.</summary>
    public const string Request = "request";

    /// <summary>The <c>none</c> uniqueness characteristic.</summary>
    public const string UniquenessNone = "none";

    /// <summary>The <c>server</c> uniqueness characteristic.</summary>
    public const string UniquenessServer = "server";

    /// <summary>The <c>global</c> uniqueness characteristic.</summary>
    public const string UniquenessGlobal = "global";

    /// <summary>The <c>invalidFilter</c> error type.</summary>
    public const string InvalidFilter = "invalidFilter";

    /// <summary>The <c>tooMany</c> error type.</summary>
    public const string TooMany = "tooMany";

    /// <summary>The <c>uniqueness</c> error type.</summary>
    public const string Uniqueness = "uniqueness";

    /// <summary>The <c>mutability</c> error type.</summary>
    public const string Mutability = "mutability";

    /// <summary>The <c>invalidSyntax</c> error type.</summary>
    public const string InvalidSyntax = "invalidSyntax";

    /// <summary>The detail returned when no SCIM input formatter accepts the request media type.</summary>
    public const string UnsupportedMediaTypeDetail =
        "The request media type is not supported; use application/scim+json.";

    /// <summary>The <c>invalidPath</c> error type.</summary>
    public const string InvalidPath = "invalidPath";

    /// <summary>The <c>noTarget</c> error type.</summary>
    public const string NoTarget = "noTarget";

    /// <summary>The <c>invalidValue</c> error type.</summary>
    public const string InvalidValue = "invalidValue";

    /// <summary>The <c>invalidVers</c> error type.</summary>
    public const string InvalidVers = "invalidVers";

    /// <summary>The <c>sensitive</c> error type.</summary>
    public const string Sensitive = "sensitive";
}
