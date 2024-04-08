// Licensed to the .NET Foundation under one or more agreements.
// https://github.com/dotnet/aspnetcore/tree/37a0667cf150baa4aec2d605dbe06fffaac25f04/src/Security/Authentication/BearerToken/src

#if NET6_0
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Authentication.BearerToken;

internal sealed class BearerTokenConfigureJsonOptions : IConfigureOptions<JsonOptions>
{
    #region IConfigureOptions<JsonOptions> Members

    public void Configure(JsonOptions options) {
        // Put our resolver in front of the reflection-based one. See ProblemDetailsOptionsSetup for a detailed explanation.
        options.SerializerOptions.TypeInfoResolverChain.Insert(0, BearerTokenJsonSerializerContext.Default);
    }

    #endregion
}
#endif
