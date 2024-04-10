// Licensed to the .NET Foundation under one or more agreements.
// https://github.com/dotnet/aspnetcore/tree/37a0667cf150baa4aec2d605dbe06fffaac25f04/src/Security/Authentication/BearerToken/src

#if NET6_0
using System.Text.Json.Serialization;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Authentication.BearerToken;

[JsonSerializable(typeof(AccessTokenResponse))]
internal sealed partial class BearerTokenJsonSerializerContext : JsonSerializerContext;
#endif
