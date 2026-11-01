using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Session;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Core.State;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.State;

/// <summary>
///     Registration-only builder handed to <see cref="TenantStateExtensions.UseState{TTenant}" />
///     callbacks while a tenant service collection is composed.
/// </summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public sealed class TenantStateBuilder<TTenant>
    where TTenant : SchemataTenant
{
    /// <summary>
    ///     Creates a builder over the tenant service collection for a verified tenant.
    /// </summary>
    /// <param name="services">The tenant service collection.</param>
    /// <param name="tenantUid">The verified tenant identifier every registration is bound to.</param>
    /// <param name="host">The host provider that owns the global capability a tenant branch excludes.</param>
    public TenantStateBuilder(IServiceCollection services, Guid tenantUid, IServiceProvider host) {
        Services  = services;
        TenantUid = tenantUid;
        Host      = host;
    }

    private readonly List<AuthenticationSchemeScope> _schemes = [];
    private bool _session;
    private bool _antiforgery;

    /// <summary>Gets the tenant service collection receiving per-tenant registrations.</summary>
    public IServiceCollection Services { get; }

    /// <summary>Gets the host provider that owns the global capability a tenant branch excludes.</summary>
    public IServiceProvider Host { get; }

    /// <summary>Gets the verified tenant identifier every registration is bound to.</summary>
    public Guid TenantUid { get; }

    /// <summary>
    ///     Gets or sets the cookie namespace prefix of the composed
    ///     <see cref="TenantStateProfile" />; it is required so cookies stay distinguishable
    ///     between tenants on the same origin.
    /// </summary>
    public string? CookiePrefix { get; set; }

    /// <summary>
    ///     Declares a tenant-owned cookie scheme. The scheme is registered once per profile
    ///     with the native <see cref="CookieAuthenticationHandler" />, its cookie is named
    ///     <c>CookiePrefix + scheme</c> and protected by the profile's fixed provider, and
    ///     authentication operations on the scheme run inside the bound tenant scope.
    ///     <paramref name="configure" /> may set ticket store, manager, and cookie
    ///     attributes; the framework owns the cookie name and protection.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     <see cref="CookiePrefix" /> was not set, or <paramref name="scheme" /> was
    ///     already declared by this profile.
    /// </exception>
    public void UseCookie(string scheme, Action<CookieAuthenticationOptions> configure) {
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        if (string.IsNullOrWhiteSpace(CookiePrefix)) {
            throw new InvalidOperationException(
                $"Tenant state for '{TenantUid:N}' requires an explicit cookie prefix before UseCookie; without one cookies cannot stay distinguishable between tenants.");
        }
        if (_schemes.Any(owned => string.Equals(owned.Scheme, scheme, StringComparison.Ordinal))) {
            throw new InvalidOperationException(
                $"Authentication scheme '{scheme}' is already declared by tenant state '{TenantUid:N}'; scheme ownership is declared once.");
        }

        var profile = new TenantStateProfile(TenantUid, CookiePrefix);
        _schemes.Add(new(scheme, TenantAuthenticationRegistration.ServiceKey));
        TenantCookieRegistration.Register(Services, scheme, configure, profile);
        Services.TryAddSingleton<IAuthenticationScopeProvider>(new TenantAuthenticationScope(_schemes));
    }

    /// <summary>
    ///     Declares the tenant-owned Identity bearer scheme: the native
    ///     <see cref="BearerTokenHandler" /> in the tenant scheme table with closed
    ///     <see cref="BearerTokenOptions" /> whose access and refresh protectors derive from the
    ///     profile's fixed data protection provider. Access and refresh tokens issued here are
    ///     bound to this profile's key material, so a refresh token presented to a sibling
    ///     profile or to the host bearer scheme fails to unprotect and the refresh endpoint
    ///     challenges. <paramref name="configure" /> may set token lifetimes and events; the
    ///     framework owns the protection boundary.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     <see cref="CookiePrefix" /> was not set, or <paramref name="scheme" /> was
    ///     already declared by this profile.
    /// </exception>
    public void UseBearer(string scheme, Action<BearerTokenOptions> configure) {
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        if (string.IsNullOrWhiteSpace(CookiePrefix)) {
            throw new InvalidOperationException(
                $"Tenant state for '{TenantUid:N}' requires an explicit cookie prefix before UseBearer; without one the profile cannot be composed.");
        }
        if (_schemes.Any(owned => string.Equals(owned.Scheme, scheme, StringComparison.Ordinal))) {
            throw new InvalidOperationException(
                $"Authentication scheme '{scheme}' is already declared by tenant state '{TenantUid:N}'; scheme ownership is declared once.");
        }

        _schemes.Add(new(scheme, TenantAuthenticationRegistration.ServiceKey));
        TenantBearerRegistration.Register(Services, scheme, configure);
        Services.TryAddSingleton<IAuthenticationScopeProvider>(new TenantAuthenticationScope(_schemes));
    }

    /// <summary>
    ///     Declares the tenant-owned session capability: a closed native session graph in the
    ///     tenant collection and the native <c>UseSession</c> branch of
    ///     <see cref="DefaultStatePipelineProvider" /> bound to <typeparamref name="TStore" />,
    ///     whose dependencies resolve from this closure. The session-id cookie is named
    ///     <c>CookiePrefix + SessionDefaults.CookieName</c> and protected by the profile's
    ///     fixed data protection provider, so a cookie or session identifier issued by
    ///     another profile starts a fresh session here. The global capability keeps reading
    ///     the global provider; each request selects exactly one branch and carries one
    ///     session.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     <see cref="CookiePrefix" /> was not set, the capability was already declared, or
    ///     <typeparamref name="TStore" /> is the native <see cref="DistributedSessionStore" />
    ///     without a configured tenant <see cref="IDistributedCache" />.
    /// </exception>
    public void UseSession<TStore>(Action<SessionOptions> configure)
        where TStore : class, ISessionStore {
        ArgumentNullException.ThrowIfNull(configure);
        if (_session) {
            throw new InvalidOperationException(
                $"Tenant state for '{TenantUid:N}' already declares a session capability; session ownership is declared once.");
        }
        var profile = CreateProfile();
        if (typeof(TStore) == typeof(DistributedSessionStore)
            && !Services.Any(descriptor => descriptor.ServiceType == typeof(IDistributedCache))) {
            throw new InvalidOperationException(
                $"Tenant session for '{TenantUid:N}' declares {nameof(DistributedSessionStore)}, which requires a configured {nameof(IDistributedCache)}; declare UseSessionCache on the tenant cache builder, or register a store, before UseSession.");
        }

        _session = true;
        TenantSessionRegistration.Register<TStore>(Services, configure, profile);
    }

    /// <summary>
    ///     Declares the tenant-owned antiforgery capability: the native antiforgery service
    ///     graph transplanted from the public <c>AddAntiforgery</c> registration into this
    ///     closure, plus the native <c>UseAntiforgery</c> branch of
    ///     <see cref="DefaultStatePipelineProvider" />, so endpoint metadata validation and
    ///     direct <see cref="IAntiforgery" /> consumers resolve the profile's protector and
    ///     cookie namespace. The antiforgery cookie is named
    ///     <c>CookiePrefix +</c> the native name derived from the application discriminator,
    ///     so a token pair issued by another profile never validates here. The global
    ///     capability keeps its own graph; each request selects exactly one branch, and the
    ///     global antiforgery registration must be present for the branch selection.
    ///     <paramref name="configure" /> may set cookie attributes and header or form field
    ///     names; the framework owns the cookie name namespace. An application
    ///     <see cref="IAntiforgeryAdditionalDataProvider" /> registered on
    ///     <see cref="Services" /> before this call keeps its native <c>TryAdd</c> priority.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     <see cref="CookiePrefix" /> was not set, or the capability was already declared.
    /// </exception>
    public void UseAntiforgery(Action<AntiforgeryOptions> configure) {
        ArgumentNullException.ThrowIfNull(configure);
        if (_antiforgery) {
            throw new InvalidOperationException(
                $"Tenant state for '{TenantUid:N}' already declares an antiforgery capability; antiforgery ownership is declared once.");
        }
        var profile = CreateProfile();

        _antiforgery = true;
        TenantAntiforgeryRegistration.Register(Services, configure, profile, Host);
    }

    /// <summary>Creates the profile this builder composed.</summary>
    /// <exception cref="InvalidOperationException">The cookie prefix was not supplied.</exception>
    internal TenantStateProfile CreateProfile() {
        if (string.IsNullOrWhiteSpace(CookiePrefix)) {
            throw new InvalidOperationException(
                $"Tenant state for '{TenantUid:N}' requires an explicit cookie prefix; without one cookies cannot stay distinguishable between tenants.");
        }
        return new(TenantUid, CookiePrefix!);
    }
}
