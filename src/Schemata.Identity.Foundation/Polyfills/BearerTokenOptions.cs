// Licensed to the .NET Foundation under one or more agreements.
// https://github.com/dotnet/aspnetcore/tree/37a0667cf150baa4aec2d605dbe06fffaac25f04/src/Security/Authentication/BearerToken/src

#if NET6_0
using System;
using Microsoft.AspNetCore.DataProtection;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Authentication.BearerToken;

/// <summary>
///     Contains the options used to authenticate using opaque bearer tokens.
/// </summary>
public sealed class BearerTokenOptions : AuthenticationSchemeOptions
{
    private ISecureDataFormat<AuthenticationTicket>? _bearerTokenProtector;
    private ISecureDataFormat<AuthenticationTicket>? _refreshTokenProtector;

    /// <summary>
    ///     Constructs the options used to authenticate using opaque bearer tokens.
    /// </summary>
    public BearerTokenOptions() {
        Events = new();
    }

    /// <summary>
    ///     Controls how much time the bearer token will remain valid from the point it is created.
    ///     The expiration information is stored in the protected token. Because of that, an expired token will be rejected
    ///     even if it is passed to the server after the client should have purged it.
    /// </summary>
    /// <remarks>
    ///     Defaults to 1 hour.
    /// </remarks>
    public TimeSpan BearerTokenExpiration { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    ///     Controls how much time the refresh token will remain valid from the point it is created.
    ///     The expiration information is stored in the protected token.
    /// </summary>
    /// <remarks>
    ///     Defaults to 14 days.
    /// </remarks>
    public TimeSpan RefreshTokenExpiration { get; set; } = TimeSpan.FromDays(14);

    /// <summary>
    ///     If set, the <see cref="BearerTokenProtector" /> is used to protect and unprotect the identity and other properties
    ///     which are stored in the
    ///     bearer token. If not provided, one will be created using <see cref="TicketDataFormat" /> and the
    ///     <see cref="IDataProtectionProvider" />
    ///     from the application <see cref="IServiceProvider" />.
    /// </summary>
    public ISecureDataFormat<AuthenticationTicket> BearerTokenProtector
    {
        get => _bearerTokenProtector
            ?? throw new InvalidOperationException($"{nameof(BearerTokenProtector)} was not set.");
        set => _bearerTokenProtector = value;
    }

    /// <summary>
    ///     If set, the <see cref="RefreshTokenProtector" /> is used to protect and unprotect the identity and other properties
    ///     which are stored in the
    ///     refresh token. If not provided, one will be created using <see cref="TicketDataFormat" /> and the
    ///     <see cref="IDataProtectionProvider" />
    ///     from the application <see cref="IServiceProvider" />.
    /// </summary>
    public ISecureDataFormat<AuthenticationTicket> RefreshTokenProtector
    {
        get => _refreshTokenProtector
            ?? throw new InvalidOperationException($"{nameof(RefreshTokenProtector)} was not set.");
        set => _refreshTokenProtector = value;
    }

    /// <summary>
    ///     The object provided by the application to process events raised by the bearer token authentication handler.
    ///     The application may implement the interface fully, or it may create an instance of <see cref="BearerTokenEvents" />
    ///     and assign delegates only to the events it wants to process.
    /// </summary>
    public new BearerTokenEvents Events
    {
        get => (BearerTokenEvents)base.Events!;
        set => base.Events = value;
    }
}
#endif
