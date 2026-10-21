using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Tenancy.Foundation.State;

/// <summary>
///     Activates authentication handlers through an explicitly selected scope instead of
///     <see cref="HttpContext.RequestServices" />, so each handler reads the options and
///     services that the scope owning its scheme registered. One handler per scheme is kept
///     for the lifetime of that scope.
/// </summary>
/// <remarks>
///     The framework creates an events instance declared through
///     <see cref="AuthenticationSchemeOptions.EventsType" /> from
///     <see cref="HttpContext.RequestServices" /> while initializing a handler; a scheme that
///     needs a scope-bound events instance registers its handler in the owning scope.
/// </remarks>
/// <param name="services">The scope whose registrations construct and initialize the handlers.</param>
/// <param name="schemes">The scheme table that resolves handler types for <paramref name="services" />.</param>
public sealed class ScopedAuthenticationHandlerProvider(
    IServiceProvider              services,
    IAuthenticationSchemeProvider schemes
) : IAuthenticationHandlerProvider
{
    private readonly ConcurrentDictionary<string, Lazy<Task<IAuthenticationHandler?>>> _handlers = new(StringComparer.Ordinal);

    #region IAuthenticationHandlerProvider Members

    /// <inheritdoc />
    public async Task<IAuthenticationHandler?> GetHandlerAsync(HttpContext context, string authenticationScheme) {
        var handler = _handlers.GetOrAdd(
            authenticationScheme,
            name => new Lazy<Task<IAuthenticationHandler?>>(
                () => CreateHandlerAsync(context, name),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try {
            return await handler.Value;
        } catch {
            if (_handlers.TryGetValue(authenticationScheme, out var current) && ReferenceEquals(current, handler)) {
                _handlers.TryRemove(authenticationScheme, out _);
            }
            throw;
        }
    }

    #endregion

    private async Task<IAuthenticationHandler?> CreateHandlerAsync(HttpContext context, string authenticationScheme) {
        var scheme = await schemes.GetSchemeAsync(authenticationScheme);
        if (scheme is null) {
            return null;
        }

        var handler = (services.GetService(scheme.HandlerType)
            ?? ActivatorUtilities.CreateInstance(services, scheme.HandlerType)) as IAuthenticationHandler;
        if (handler is not null) {
            await handler.InitializeAsync(scheme, context);
        }

        return handler;
    }
}
