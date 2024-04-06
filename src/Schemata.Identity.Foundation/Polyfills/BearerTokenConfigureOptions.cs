// Licensed to the .NET Foundation under one or more agreements.

#if NET6_0
using System;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Authentication.BearerToken;

internal sealed class BearerTokenConfigureOptions(IDataProtectionProvider dp)
    : IConfigureNamedOptions<BearerTokenOptions>
{
    private const string PrimaryPurpose = "Microsoft.AspNetCore.Authentication.BearerToken";

    #region IConfigureNamedOptions<BearerTokenOptions> Members

    public void Configure(string? schemeName, BearerTokenOptions options) {
        if (schemeName is null) {
            return;
        }

        options.BearerTokenProtector
            = new TicketDataFormat(dp.CreateProtector(PrimaryPurpose, schemeName, "BearerToken"));
        options.RefreshTokenProtector
            = new TicketDataFormat(dp.CreateProtector(PrimaryPurpose, schemeName, "RefreshToken"));
    }

    public void Configure(BearerTokenOptions options) {
        throw new NotImplementedException();
    }

    #endregion
}
#endif
