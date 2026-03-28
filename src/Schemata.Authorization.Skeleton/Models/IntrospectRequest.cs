namespace Schemata.Authorization.Skeleton.Models;

public class IntrospectRequest
{
    /// <summary>Token value to introspect per RFC 7662 section 2.1.</summary>
    public string? Token { get; set; }

    /// <summary>Hint about the type of token, e.g. "access_token" or "refresh_token" per RFC 7662 section 2.1.</summary>
    public string? TokenTypeHint { get; set; }

    /// <summary>Client identifier for authentication when using the request body per RFC 6749 section 2.3.1.</summary>
    public string? ClientId { get; set; }

    /// <summary>Client secret for authentication when using the request body per RFC 6749 section 2.3.1.</summary>
    public string? ClientSecret { get; set; }
}
