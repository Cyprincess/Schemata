using Microsoft.AspNetCore.Authentication;

namespace Schemata.Authorization.Foundation.Authentication;

/// <summary>
///     Typed wrapper for <see cref="AuthenticationSchemeOptions" />.
///     Exists so the Schemata handlers can accept their own options type
///     without polluting the standard ASP.NET Core options.
/// </summary>
public class SchemataAuthenticationHandlerOptions : AuthenticationSchemeOptions;
