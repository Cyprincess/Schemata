using System;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Security.Skeleton;

/// <summary>Domain-supplied security wiring consumed by the shared builder extensions.</summary>
public sealed record ResourceSecurityRegistration(
    Action<IServiceCollection> AddAuthentication,
    Action<IServiceCollection> AddAuthorization,
    Action<string?>            SetScheme);
