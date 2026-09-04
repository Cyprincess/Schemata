using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
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
public sealed class PairwiseSubjectTranslator<TApp> : IPairwiseSubjectTranslator
    where TApp : SchemataApplication
{
    private readonly IApplicationManager<TApp>           _apps;
    private readonly IRepository<SchemataSubjectMapping> _mappings;
    private readonly ISubjectIdentifierService           _subjects;

    public PairwiseSubjectTranslator(
        IApplicationManager<TApp>           apps,
        ISubjectIdentifierService           subjects,
        IRepository<SchemataSubjectMapping> mappings
    ) {
        _apps     = apps;
        _subjects = subjects;
        _mappings = mappings;
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

        var subjectType = application.SubjectType ?? SubjectTypes.Public;
        if (subjectType != SubjectTypes.Pairwise) {
            return subject;
        }

        var key = application.CanonicalName ?? application.Name;
        var mapping = await _mappings.FirstOrDefaultAsync(
                          q => q.Where(m => m.Application == key && m.PairwiseSubject == subject),
                          ct);
        return mapping?.Subject;
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

        var subjectType = application.SubjectType ?? SubjectTypes.Public;
        if (subjectType != SubjectTypes.Pairwise) {
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
        var subjectType = application.SubjectType ?? SubjectTypes.Public;
        if (subjectType != SubjectTypes.Pairwise) {
            return subject;
        }

        if (string.IsNullOrWhiteSpace(application.CanonicalName)) {
            throw new InvalidOperationException(
                $"Application '{application.Name ?? application.ClientId}' has no canonical name; "
              + "pairwise subject mapping requires a fully resolved resource name.");
        }

        var key = application.CanonicalName;

        var existing = await _mappings.FirstOrDefaultAsync(
                           q => q.Where(m => m.Application == key && m.Subject == subject),
                           ct);
        if (existing is { PairwiseSubject: { Length: > 0 } stored }) {
            return stored;
        }

        var pairwise = _subjects.Resolve(subject, application);
        if (existing is not null) {
            return pairwise;
        }

        await _mappings.AddAsync(new() {
            Application     = key,
            Subject         = subject,
            PairwiseSubject = pairwise,
            SectorHost      = TryGetSector(application),
        }, ct);
        await _mappings.CommitAsync(ct);

        return pairwise;
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
