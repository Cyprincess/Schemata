// Licensed to the .NET Foundation under one or more agreements.
// https://github.com/dotnet/aspnetcore/tree/37a0667cf150baa4aec2d605dbe06fffaac25f04/src/Security/Authentication/BearerToken/src

#if NET6_0
// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Authentication.BearerToken;

/// <summary>
///     Default values used by bearer token authentication.
/// </summary>
public static class BearerTokenDefaults
{
    /// <summary>
    ///     Default value for AuthenticationScheme property in the <see cref="BearerTokenOptions" />.
    /// </summary>
    public const string AuthenticationScheme = "BearerToken";
}
#endif
