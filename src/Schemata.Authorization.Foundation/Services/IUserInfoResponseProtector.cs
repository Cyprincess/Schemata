using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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

/// <summary>
///     Signs and/or encrypts UserInfo responses for clients that registered
///     <c>userinfo_signed_response_alg</c> / <c>userinfo_encrypted_response_alg</c>
///     (Core 1.0 §5.3.2). Returns <see langword="null" /> for clients without either
///     registration, leaving the response as plain JSON.
/// </summary>
public interface IUserInfoResponseProtector
{
    /// <summary>
    ///     Protects the assembled claim set for the named client. Returns <see langword="null" />
    ///     when the client registered neither a signing nor an encryption algorithm.
    /// </summary>
    /// <param name="clientId">The client the response is addressed to.</param>
    /// <param name="claims">The assembled UserInfo claim set.</param>
    /// <param name="ct">A cancellation token.</param>
    Task<UserInfoJwt?> ProtectAsync(
        string?                        clientId,
        IReadOnlyDictionary<string, object> claims,
        CancellationToken              ct = default);
}
