// Licensed to the .NET Foundation under one or more agreements.
// https://github.com/dotnet/aspnetcore/tree/37a0667cf150baa4aec2d605dbe06fffaac25f04/src/Security/Authentication/BearerToken/src

#if NET6_0
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Logging;

internal static partial class LoggingExtensions
{
    [LoggerMessage(1, LogLevel.Information, "AuthenticationScheme: {AuthenticationScheme} signed in.", EventName = "AuthenticationSchemeSignedIn")]
    public static partial void AuthenticationSchemeSignedIn(this ILogger logger, string authenticationScheme);
}
#endif
