// Licensed to the .NET Foundation under one or more agreements.
// https://github.com/dotnet/aspnetcore/tree/37a0667cf150baa4aec2d605dbe06fffaac25f04/src/Security/Authentication/BearerToken/src

#if NET6_0
using System;
using System.Threading.Tasks;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Authentication.BearerToken;

/// <summary>
///     Specifies events which the bearer token handler invokes to enable developer control over the authentication
///     process.
/// </summary>
public class BearerTokenEvents
{
    /// <summary>
    ///     Invoked when a protocol message is first received.
    /// </summary>
    public Func<MessageReceivedContext, Task> OnMessageReceived { get; set; } = _ => Task.CompletedTask;

    /// <summary>
    ///     Invoked when a protocol message is first received.
    /// </summary>
    /// <param name="context">The <see cref="MessageReceivedContext" />.</param>
    public virtual Task MessageReceivedAsync(MessageReceivedContext context) {
        return OnMessageReceived(context);
    }
}
#endif
