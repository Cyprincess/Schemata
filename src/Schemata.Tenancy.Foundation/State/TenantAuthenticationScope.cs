using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Schemata.Core.State;
using Schemata.Tenancy.Foundation.Middlewares;
using Schemata.Tenancy.Foundation.Services;

namespace Schemata.Tenancy.Foundation.State;

/// <summary>
///     Routes schemes declared by one tenant state profile to the bound tenant scope and
///     every other scheme to the host request scope preserved by the tenancy middleware.
///     A declared scheme without a bound tenant scope fails here instead of reaching a
///     host handler.
/// </summary>
internal sealed class TenantAuthenticationScope : IAuthenticationScopeProvider
{
    private readonly IReadOnlyList<AuthenticationSchemeScope>      _scopes;
    private          Dictionary<string, AuthenticationSchemeScope>? _table;

    /// <summary>Creates a scope router over the schemes declared while a profile composed.</summary>
    internal TenantAuthenticationScope(IReadOnlyList<AuthenticationSchemeScope> scopes) {
        _scopes = scopes;
    }

    #region IAuthenticationScopeProvider Members

    /// <inheritdoc />
    public AuthenticationScope GetScope(HttpContext context, string scheme) {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        if (Table.TryGetValue(scheme, out var declared)) {
            return context.RequestServices is CompositeScope
                ? new AuthenticationScope(context.RequestServices, declared.ServiceKey)
                : throw new InvalidOperationException(
                    $"Authentication scheme '{scheme}' is owned by tenant state and requires the bound tenant scope; the current request has none.");
        }

        var host = context.Features.Get<ITenantRequestBinding>()?.Original
            ?? throw new InvalidOperationException(
                $"Authentication scheme '{scheme}' resolves to the host request scope, which is only preserved while the tenancy middleware serves the request.");

        return new(host, ScopedAuthenticationService.HostServiceKey);
    }

    #endregion

    // Composition is single-threaded and the declaration list is immutable afterwards, so a
    // concurrent first lookup builds two equivalent tables and the last assignment wins.
    private Dictionary<string, AuthenticationSchemeScope> Table {
        get { return _table ??= Build(); }
    }

    private Dictionary<string, AuthenticationSchemeScope> Build() {
        var table = new Dictionary<string, AuthenticationSchemeScope>(_scopes.Count, StringComparer.Ordinal);
        foreach (var scope in _scopes) {
            table.Add(scope.Scheme, scope);
        }
        return table;
    }
}
