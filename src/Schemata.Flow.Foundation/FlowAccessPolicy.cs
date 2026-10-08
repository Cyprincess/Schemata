using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Tenancy;
using Schemata.Common.Errors;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Advisors;

namespace Schemata.Flow.Foundation;

public sealed class FlowAccessPolicy(
    IServiceProvider services,
    IFlowSubjectResolver subjects,
    IPermissionResolver permissions,
    IPermissionMatcher matcher,
    TimeProvider clock
) : IResourceTargetPolicy
{
    public const string Administration = "administer";

    internal bool HasPermission(string operation, Type entity, ClaimsPrincipal? principal) =>
        principal is not null && principal.Identities.Any(identity => identity.IsAuthenticated)
        && matcher.IsMatch(principal, permissions.Resolve(operation, entity));

    public Task<AccessDecision> EvaluateAsync(ResourceTarget target, ClaimsPrincipal? principal, CancellationToken ct = default) {
        if (principal is null || !principal.Identities.Any(identity => identity.IsAuthenticated)) return Task.FromResult(AccessDecision.Denied);
        if (ReferenceEquals(principal, FlowSystemPrincipal.Instance)) return Task.FromResult(AccessDecision.Allowed);
        if (target.Operation is nameof(Operations.Get) or nameof(Operations.List)) return Task.FromResult(AccessDecision.Allowed);
        var operation = target.Operation == FlowOperations.Deliver ? FlowOperations.Signal : target.Operation;
        return Task.FromResult(HasPermission(operation, target.Entity!, principal) ? AccessDecision.Allowed : AccessDecision.Denied);
    }

    internal void RequirePermission(string operation, Type entity, string? name, ClaimsPrincipal? principal) {
        if (ReferenceEquals(principal, FlowSystemPrincipal.Instance)) return;
        if (!HasPermission(operation, entity, principal)) {
            throw SchemataResourceErrors.PermissionDenied(entity, name,
                description: string.Format(SchemataResourceErrors.PermissionDeniedTemplate, permissions.Resolve(operation, entity), name ?? entity.Name));
        }
    }

    internal async Task<string[]> ReadableProcessesAsync(ClaimsPrincipal? principal, CancellationToken ct) {
        if (principal is null) return [];
        var resolved = await subjects.ResolveAsync(principal, ct);
        var identities = resolved.Actor is { } actor ? resolved.Groups.Append(actor).ToArray() : [];
        if (identities.Length == 0) return [];
        var tenant = TenantContext.Current.Uid;
        var now = clock.GetUtcNow().UtcDateTime;
        var participants = services.GetRequiredService<IRepository<SchemataProcessParticipant>>();
        var rows = await participants.ListAsync(query => query.Where(row => row.TenantUid == tenant
            && identities.Contains(row.Subject) && row.Kind == ProcessParticipationKind.Participation
            && row.RevokedAt == null && (row.ExpiresAt == null || row.ExpiresAt > now))
            .Select(row => row.Process).Distinct(), ct).ToListAsync(ct);
        return rows.ToArray();
    }

    internal async Task<bool> CanReadAsync(string process, Type entity, string operation, ClaimsPrincipal? principal, CancellationToken ct) {
        if (HasPermission(operation, entity, principal)) return true;
        return (await ReadableProcessesAsync(principal, ct)).Contains(process, StringComparer.Ordinal);
    }

    internal async Task RequireEligibilityAsync(
        string operation, SchemataProcess process, IReadOnlyList<SchemataProcessToken> targets,
        ClaimsPrincipal? principal, FlowPersistenceScope scope, CancellationToken ct) {
        var entity = operation == FlowOperations.Cancel ? typeof(SchemataProcessToken) : typeof(SchemataProcess);
        RequirePermission(operation, entity, process.CanonicalName, principal);
        if (process.TenantUid != TenantContext.Current.Uid) Deny();
        if (ReferenceEquals(principal, FlowSystemPrincipal.Instance)) return;
        if (HasPermission(Administration, typeof(SchemataProcess), principal)) return;
        if (ProcessStates.IsTerminal(process.State)) Deny();
        var resolved = await subjects.ResolveAsync(principal!, ct);
        var identities = resolved.Actor is { } actor ? resolved.Groups.Append(actor).ToArray() : [];
        var now = clock.GetUtcNow().UtcDateTime;
        var participants = services.GetRequiredService<IRepository<SchemataProcessParticipant>>();
        participants.Join(scope.UnitOfWork);
        var rows = await participants.ListAsync(query => query.Where(row => row.Process == process.CanonicalName
            && row.TenantUid == process.TenantUid && identities.Contains(row.Subject)
            && row.Kind == ProcessParticipationKind.Eligibility && row.RevokedAt == null
            && (row.ExpiresAt == null || row.ExpiresAt > now)), ct).ToListAsync(ct);
        if (operation == FlowOperations.Terminate) {
            if (!rows.Any(row => row.Token == null && row.Activity == null)) Deny();
            return;
        }
        if (targets.Count == 0) Deny();
        foreach (var token in targets) {
            if (token.Process != process.Name || token.TenantUid != process.TenantUid || !TokenStates.Live.Contains(token.State!)) Deny();
            if (!rows.Any(row => row.Token == token.CanonicalName
                && row.Activity == (token.WaitingAtName ?? token.StateName))) Deny();
        }
        void Deny() => throw SchemataResourceErrors.PermissionDenied(entity, process.CanonicalName,
            description: string.Format(SchemataResourceErrors.PermissionDeniedTemplate, permissions.Resolve(operation, entity), process.CanonicalName));
    }

    internal async Task RecordParticipationAsync(SchemataProcess process, ClaimsPrincipal? principal, FlowPersistenceScope scope, CancellationToken ct) {
        if (principal is null) return;
        var subject = (await subjects.ResolveAsync(principal, ct)).Actor;
        if (string.IsNullOrWhiteSpace(subject)) return;
        var participants = services.GetRequiredService<IRepository<SchemataProcessParticipant>>();
        participants.Join(scope.UnitOfWork);
        if (await participants.AnyAsync(query => query.Where(row => row.Process == process.CanonicalName
            && row.TenantUid == process.TenantUid && row.Subject == subject
            && row.Kind == ProcessParticipationKind.Participation), ct)) return;
        await services.GetRequiredService<IResourceMutation<SchemataProcessParticipant>>().CreateAsync(new() {
            Process = process.CanonicalName!, TenantUid = process.TenantUid, Subject = subject,
            Kind = ProcessParticipationKind.Participation,
        }, scope.UnitOfWork, ct);
    }
}
