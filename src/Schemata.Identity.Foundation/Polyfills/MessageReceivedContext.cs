// Licensed to the .NET Foundation under one or more agreements.
// https://github.com/dotnet/aspnetcore/tree/37a0667cf150baa4aec2d605dbe06fffaac25f04/src/Security/Authentication/BearerToken/src

#if NET6_0
using Microsoft.AspNetCore.Http;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Authentication.BearerToken;

/// <summary>
///     A context for <see cref="BearerTokenEvents.OnMessageReceived" />.
/// </summary>
public class MessageReceivedContext : ResultContext<BearerTokenOptions>
{
    /// <summary>
    ///     Initializes a new instance of <see cref="MessageReceivedContext" />.
    /// </summary>
    /// <inheritdoc />
    public MessageReceivedContext(HttpContext context, AuthenticationScheme scheme, BearerTokenOptions options) : base(context, scheme, options) { }

    /// <summary>
    ///     Bearer Token. This will give the application an opportunity to retrieve a token from an alternative location.
    /// </summary>
    public string? Token { get; set; }
}
#endif
