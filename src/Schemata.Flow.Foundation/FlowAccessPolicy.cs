using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Common.Errors;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Advisors;

namespace Schemata.Flow.Foundation;

/// <summary>
///     Target and eligibility policy over the host-selected process, token, and participant entity
///     types.
/// </summary>
/// <typeparam name="TProcess">The persisted process entity type registered by the host.</typeparam>
/// <typeparam name="TToken">The persisted token entity type registered by the host.</typeparam>
/// <typeparam name="TParticipant">The persisted participant entity type registered by the host.</typeparam>
public sealed class FlowAccessPolicy<TProcess, TToken, TParticipant>(
    IServiceProvider services,
    IFlowSubjectResolver subjects,
    IPermissionResolver permissions,
    IPermissionMatcher matcher,
    TimeProvider clock
) : IResourceTargetPolicy
    where TProcess : SchemataProcess
    where TToken : SchemataProcessToken
    where TParticipant : SchemataProcessParticipant, new()
{
    public const string Administration = "administer";

    // Permission names follow the resolved entity type (`tenant-process.start` for a host-defined
    // process), matching the resource permissions the host registers for that type.
    internal bool HasPermission(string operation, Type entity, ClaimsPrincipal? principal) =>
        principal is not null && principal.Identities.Any(identity => identity.IsAuthenticated)
        && matcher.IsMatch(principal, permissions.Resolve(operation, entity));

    public Task<AccessDecision> EvaluateAsync(ResourceTarget target, ClaimsPrincipal? principal, CancellationToken ct = default) {
        if (principal is null || !principal.Identities.Any(identity => identity.IsAuthenticated)) return Task.FromResult(AccessDecision.Denied);
        if (target.Operation is nameof(Operations.Get) or nameof(Operations.List)) return Task.FromResult(AccessDecision.Allowed);
        var operation = target.Operation == FlowOperations.Deliver ? FlowOperations.Signal : target.Operation;
        return Task.FromResult(HasPermission(operation, target.Entity!, principal) ? AccessDecision.Allowed : AccessDecision.Denied);
    }

    internal void RequirePermission(string operation, Type entity, string? name, ClaimsPrincipal? principal) {
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
        var now = clock.GetUtcNow().UtcDateTime;
        var participants = services.GetRequiredService<IRepository<TParticipant>>();
        var rows = await participants.ListAsync(query => query.Where(row => identities.Contains(row.Subject) && row.Kind == ProcessParticipationKind.Participation
            && row.RevokedAt == null && (row.ExpiresAt == null || row.ExpiresAt > now))
            .Select(row => row.Process).Distinct(), ct).ToListAsync(ct);
        return rows.ToArray();
    }

    internal async Task<bool> CanReadAsync(string process, Type entity, string operation, ClaimsPrincipal? principal, CancellationToken ct) {
        if (HasPermission(operation, entity, principal)) return true;
        return (await ReadableProcessesAsync(principal, ct)).Contains(process, StringComparer.Ordinal);
    }

    internal async Task RequireEligibilityAsync(
        string                   operation,
        TProcess                 process,
        IReadOnlyList<TToken>    targets,
        ClaimsPrincipal?         principal,
        IUnitOfWork              unitOfWork,
        CancellationToken        ct
    ) {
        var entity = operation == FlowOperations.Cancel ? typeof(TToken) : typeof(TProcess);
        RequirePermission(operation, entity, process.CanonicalName, principal);
        if (HasPermission(Administration, typeof(TProcess), principal)) return;
        if (ProcessStates.IsTerminal(process.State)) Deny();
        var resolved = await subjects.ResolveAsync(principal!, ct);
        var identities = resolved.Actor is { } actor ? resolved.Groups.Append(actor).ToArray() : [];
        var now = clock.GetUtcNow().UtcDateTime;
        var participants = services.GetRequiredService<IRepository<TParticipant>>();
        participants.Join(unitOfWork);
        var rows = await participants.ListAsync(query => query.Where(row => row.Process == process.CanonicalName
            && identities.Contains(row.Subject)
            && row.Kind == ProcessParticipationKind.Eligibility && row.RevokedAt == null
            && (row.ExpiresAt == null || row.ExpiresAt > now)), ct).ToListAsync(ct);
        if (operation == FlowOperations.Terminate) {
            if (!rows.Any(row => row.Token == null && row.Activity == null)) Deny();
            return;
        }
        if (targets.Count == 0) Deny();
        foreach (var token in targets) {
            if (token.Process != process.Name || !TokenStates.Live.Contains(token.State!)) Deny();
            if (!rows.Any(row => row.Token == token.CanonicalName
                && row.Activity == (token.WaitingAtName ?? token.StateName))) Deny();
        }
        void Deny() => throw SchemataResourceErrors.PermissionDenied(entity, process.CanonicalName,
            description: string.Format(SchemataResourceErrors.PermissionDeniedTemplate, permissions.Resolve(operation, entity), process.CanonicalName));
    }

    internal async Task RecordParticipationAsync(
        TProcess          process,
        ClaimsPrincipal?  principal,
        IUnitOfWork       unitOfWork,
        CancellationToken ct
    ) {
        if (principal is null) return;
        var subject = (await subjects.ResolveAsync(principal, ct)).Actor;
        if (string.IsNullOrWhiteSpace(subject)) return;
        var participants = services.GetRequiredService<IRepository<TParticipant>>();
        participants.Join(unitOfWork);
        if (await participants.AnyAsync(query => query.Where(row => row.Process == process.CanonicalName
            && row.Subject == subject
            && row.Kind == ProcessParticipationKind.Participation), ct)) return;
        await services.GetRequiredService<IResourceMutation<TParticipant>>().CreateAsync(new() {
            Process = process.CanonicalName!, Subject = subject,
            Kind = ProcessParticipationKind.Participation,
        }, unitOfWork, ct);
    }
}
