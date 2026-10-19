namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     A UserInfo response protected per the client's registration, per
///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#UserInfoResponse">
///         OpenID Connect Core 1.0 §5.3.2: Successful UserInfo Response
///     </seealso>
///     : the claims are returned as a signed and/or encrypted JWT with the
///     <c>application/jwt</c> content type instead of plain JSON.
/// </summary>
public sealed class UserInfoJwt(string value)
{
    /// <summary>The media type of a JWT-formatted UserInfo response (Core 1.0 §5.3.2).</summary>
    public const string ContentType = "application/jwt";

    /// <summary>The compact-serialized JWT value.</summary>
    public string Value { get; } = value;
}