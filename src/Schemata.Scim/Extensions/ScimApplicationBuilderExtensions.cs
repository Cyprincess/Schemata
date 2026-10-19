using Schemata.Scim.Middleware;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Installs SCIM protocol error handling.
/// </summary>
public static class ScimApplicationBuilderExtensions
{
    /// <summary>
    ///     Converts unhandled exceptions and empty 404/405 responses under the SCIM base path into
    ///     SCIM error bodies. Register after the host exception handler so SCIM requests are
    ///     converted before the AIP-193 envelope.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    public static IApplicationBuilder UseSchemataScimErrors(this IApplicationBuilder app) {
        return app.UseMiddleware<ScimErrorMiddleware>();
    }
}
