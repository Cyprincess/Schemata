// Licensed to the .NET Foundation under one or more agreements.
// https://github.com/dotnet/aspnetcore/tree/37a0667cf150baa4aec2d605dbe06fffaac25f04/src/Security/Authentication/BearerToken/src

#if NET6_0
using System.Text.Json;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Authentication.BearerToken;

/// <summary>
///     The JSON data transfer object for the bearer token response typically found in "/login" and "/refresh" responses.
/// </summary>
public sealed class AccessTokenResponse
{
    /// <summary>
    ///     The value is always "Bearer" which indicates this response provides a "Bearer" token
    ///     in the form of an opaque <see cref="AccessToken" />.
    /// </summary>
    /// <remarks>
    ///     This is serialized as "tokenType": "Bearer" using <see cref="JsonSerializerDefaults.Web" />.
    /// </remarks>
    public string TokenType => "Bearer";

    /// <summary>
    ///     The opaque bearer token to send as part of the Authorization request header.
    /// </summary>
    /// <remarks>
    ///     This is serialized as "accessToken": "{AccessToken}" using <see cref="JsonSerializerDefaults.Web" />.
    /// </remarks>
    public string AccessToken { get; init; } = null!;

    /// <summary>
    ///     The number of seconds before the <see cref="AccessToken" /> expires.
    /// </summary>
    /// <remarks>
    ///     This is serialized as "expiresIn": "{ExpiresInSeconds}" using <see cref="JsonSerializerDefaults.Web" />.
    /// </remarks>
    public long ExpiresIn { get; init; }

    /// <summary>
    ///     If set, this provides the ability to get a new access_token after it expires using a refresh endpoint.
    /// </summary>
    /// <remarks>
    ///     This is serialized as "refreshToken": "{RefreshToken}" using using <see cref="JsonSerializerDefaults.Web" />.
    /// </remarks>
    public string RefreshToken { get; init; } = null!;
}
#endif
