using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Entities;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Entity.Repository;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Default <see cref="IPairwiseSubjectTranslator" /> backed by a persistent
///     <see cref="SchemataSubjectMapping" /> repository.
/// </summary>
/// <remarks>
///     <para>
///         Forward (<see cref="ToPairwiseAsync" />) and the
///         <see cref="EnsureMappingAsync" /> seed (called from
///         <c>AdviceClaimsPairwise</c>) both write a row in
///         <c>SchemataSubjectMappings</c> the first time an
///         <c>(application, canonical_subject)</c> pair is seen, then return the stored
///         pairwise hash on every subsequent call. The unique indexes on
///         <c>(application, canonical_subject)</c> and
///         <c>(application, pairwise_subject)</c> enforce per-app per-subject stability
///         even under concurrent token issuance.
///     </para>
///     <para>
///         Reverse (<see cref="ToCanonicalAsync" />) is a single index-shaped query on
///         the <c>(application, pairwise_subject)</c> unique index, so user-facing
///         resource handlers reverse a pairwise <c>sub</c> in O(1) database lookups
///         regardless of process lifetime or node identity. A row is missing only when
///         the value never went through OIDC issuance on any node sharing this database,
///         in which case the method returns <see langword="null" />.
///     </para>
/// </remarks>
/// <typeparam name="TApp">The configured application entity type.</typeparam>
/// <typeparam name="TSubjectMapping">The configured pairwise subject-mapping entity type.</typeparam>
public sealed class PairwiseSubjectTranslator<TApp, TSubjectMapping> : IPairwiseSubjectTranslator
    where TApp : SchemataApplication, new()
    where TSubjectMapping : SchemataSubjectMapping, new()
{
    private readonly IApplicationManager<TApp>               _apps;
    private readonly IRepository<TSubjectMapping>            _mappings;
    private readonly IResourceMutation<TSubjectMapping>      _mutation;
    private readonly IServiceProvider                        _services;
    private readonly ISubjectIdentifierService               _subjects;

    public PairwiseSubjectTranslator(
        IApplicationManager<TApp>            apps,
        ISubjectIdentifierService            subjects,
        IRepository<TSubjectMapping>         mappings,
        IResourceMutation<TSubjectMapping>   mutation,
        IServiceProvider                     services
    ) {
        _apps     = apps;
        _subjects = subjects;
        _mappings = mappings;
        _mutation = mutation;
        _services = services;
    }

    #region IPairwiseSubjectTranslator Members

    public async Task<string?> ToCanonicalAsync(
        string?           subject,
        ClaimsPrincipal?  caller,
        CancellationToken ct = default) {
        if (string.IsNullOrWhiteSpace(subject)) {
            return subject;
        }

        var application = await ResolveCallerApplicationAsync(caller, ct);
        if (application is null) {
            return subject;
        }

        // A stored mapping resolves from the row alone: the projection was issued while the
        // application still carried its sector metadata, which a later replace may have
        // cleared. Only an unissued value needs the owner's classification.
        var key     = application.CanonicalName;
        var mapping = await _mappings.FirstOrDefaultAsync(
                          q => q.Where(m => m.Application == key && m.PairwiseSubject == subject),
                          ct);
        if (mapping is not null) {
            return mapping.Subject;
        }

        // The service owns the effective subject type (application override or host
        // default); a passthrough resolve means this value never entered pairwise
        // projection, so the value passes through as a public subject.
        if (string.Equals(_subjects.Resolve(subject, application), subject, StringComparison.Ordinal)) {
            return subject;
        }

        return null;
    }

    public async Task<string?> ToPairwiseAsync(
        string?           subject,
        ClaimsPrincipal?  caller,
        CancellationToken ct = default) {
        if (string.IsNullOrWhiteSpace(subject)) {
            return subject;
        }

        var application = await ResolveCallerApplicationAsync(caller, ct);
        if (application is null) {
            return subject;
        }

        return await EnsureMappingAsync(application, subject, ct);
    }

    #endregion

    /// <summary>
    ///     Returns the stored pairwise subject for <paramref name="subject" /> under
    ///     <paramref name="application" />, inserting a new row when one does not yet exist.
    ///     Called from <c>AdviceClaimsPairwise</c> during claim assembly so OAuth wire
    ///     endpoints (id_token, access_token, userinfo, introspection, back-channel logout)
    ///     implicitly seed the reverse-lookup table without extra plumbing.
    /// </summary>
    /// <param name="application">The OAuth application the pairwise hash is bound to.</param>
    /// <param name="subject">The canonical subject the hash projects from.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<string> EnsureMappingAsync(
        SchemataApplication application,
        string              subject,
        CancellationToken   ct = default) {
        var key = application.CanonicalName;

        // A stored mapping resolves from the row alone: the projection was issued while the
        // application still carried its sector metadata, which a later replace may have
        // cleared. Only an application without a usable row needs the owner's decision.
        TSubjectMapping? existing = null;
        if (!string.IsNullOrWhiteSpace(key)) {
            existing = await _mappings.FirstOrDefaultAsync(
                           q => q.Where(m => m.Application == key && m.Subject == subject),
                           ct);
            if (existing is { PairwiseSubject: { Length: > 0 } stored }) {
                return stored;
            }
        }

        // Single ownership: the service decides whether this application projects pairwise
        // (application override or host default). A passthrough resolve is a public-subject
        // application; the subject returns unchanged with no mapping row.
        var pairwise = _subjects.Resolve(subject, application);
        if (string.Equals(pairwise, subject, StringComparison.Ordinal)) {
            return subject;
        }

        if (string.IsNullOrWhiteSpace(key)) {
            throw new InvalidOperationException(
                $"Application '{application.Name ?? application.ClientId}' has no canonical name; "
              + "pairwise subject mapping requires a fully resolved resource name.");
        }

        if (existing is not null) {
            // An incomplete stored mapping is repaired through the mapping owner, so reverse
            // lookups resolve the same value forward issuance returns.
            existing.PairwiseSubject = pairwise;
            existing.SectorHost      = TryGetSector(application);
            var repairs = _services.GetRequiredService<IRepository<TSubjectMapping>>();
            await using var repair = repairs.Begin();
            await _mutation.UpdateAsync(existing, repair, Operations.Update, ct);
            await _apps.EnlistPublicationAsync(repair, key, ct);
            await repair.CommitAsync(ct);
            return pairwise;
        }

        var mappings = _services.GetRequiredService<IRepository<TSubjectMapping>>();
        try {
            await using var transaction = mappings.Begin();
            await _mutation.CreateAsync(new TSubjectMapping {
                Application     = key,
                Subject         = subject,
                PairwiseSubject = pairwise,
                SectorHost      = TryGetSector(application),
            }, transaction, ct);
            await _apps.EnlistPublicationAsync(transaction, key, ct);
            await transaction.CommitAsync(ct);

            return pairwise;
        } catch (AlreadyExistsException) {
            // The unique (Application, Subject) index arbitrates concurrent first issuances;
            // the loser returns the persisted winner so both agree with reverse lookup.
            var winner = await _mappings.FirstOrDefaultAsync(
                             q => q.Where(m => m.Application == key && m.Subject == subject),
                             ct);
            if (winner is { PairwiseSubject: { Length: > 0 } persisted }) {
                return persisted;
            }

            throw;
        }
    }

    private async Task<SchemataApplication?> ResolveCallerApplicationAsync(ClaimsPrincipal? caller, CancellationToken ct) {
        var client = caller?.FindFirstValue(Claims.ClientId);
        if (string.IsNullOrWhiteSpace(client)) {
            return null;
        }

        return await _apps.FindByClientIdAsync(client, ct);
    }

    private static string? TryGetSector(SchemataApplication application) {
        if (!string.IsNullOrWhiteSpace(application.SectorIdentifierUri)
         && Uri.TryCreate(application.SectorIdentifierUri, UriKind.Absolute, out var uri)) {
            return uri.Host;
        }

        var redirect = application.RedirectUris?.FirstOrDefault();
        return !string.IsNullOrWhiteSpace(redirect)
            && Uri.TryCreate(redirect, UriKind.Absolute, out var redirectUri)
            ? redirectUri.Host
            : null;
    }
}
