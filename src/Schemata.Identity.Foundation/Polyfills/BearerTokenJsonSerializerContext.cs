// Licensed to the .NET Foundation under one or more agreements.

#if NET6_0
using System.Text.Json.Serialization;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Authentication.BearerToken;

[JsonSerializable(typeof(AccessTokenResponse))]
internal sealed partial class BearerTokenJsonSerializerContext : JsonSerializerContext
{ }
#endif
