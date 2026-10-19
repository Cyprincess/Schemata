using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Foundation;

/// <summary>Grants and revokes participant rows over the host-selected entity types.</summary>
/// <typeparam name="TProcess">The persisted process entity type registered by the host.</typeparam>
/// <typeparam name="TToken">The persisted token entity type registered by the host.</typeparam>
/// <typeparam name="TParticipant">The persisted participant entity type registered by the host.</typeparam>
public sealed class FlowParticipantManager<TProcess, TToken, TParticipant>(
    IServiceProvider                     services,
    FlowAccessPolicy<TProcess, TToken, TParticipant> policy,
    TimeProvider                         clock
)
    where TProcess : SchemataProcess
    where TToken : SchemataProcessToken
    where TParticipant : SchemataProcessParticipant, new()
{
    public async Task<TParticipant> GrantAsync(
        string processName, string subject, ProcessParticipationKind kind, ClaimsPrincipal principal,
        string? tokenName = null, DateTimeOffset? expiresAt = null, CancellationToken ct = default) {
        policy.RequirePermission(FlowAccessPolicy<TProcess, TToken, TParticipant>.Administration, typeof(TProcess), processName, principal);
        if (string.IsNullOrWhiteSpace(subject) || !Enum.IsDefined(kind)) throw new InvalidArgumentException(SchemataResources.FLOW_PARTICIPANT_INVALID);
        if (kind == ProcessParticipationKind.Participation && tokenName is not null) throw new InvalidArgumentException(SchemataResources.FLOW_PARTICIPATION_PROCESS_ONLY);
        var processes = services.GetRequiredService<IRepository<TProcess>>();
        var participants = services.GetRequiredService<IRepository<TParticipant>>();
        var tokens = services.GetRequiredService<IRepository<TToken>>();
        await using var uow = processes.Begin();
        participants.Join(uow);
        tokens.Join(uow);
        var process = await processes.FirstOrDefaultAsync(q => q.Where(p => p.CanonicalName == processName), ct)
            ?? throw new NotFoundException(SchemataResources.FLOW_PROCESS_NOT_FOUND, new Dictionary<string, string?> { ["name"] = processName });
        string? activity = null;
        if (kind == ProcessParticipationKind.Eligibility && tokenName is not null) {
            var token = await tokens.FirstOrDefaultAsync(q => q.Where(t => t.CanonicalName == tokenName && t.Process == process.Name), ct);
            if (token is null || !TokenStates.Live.Contains(token.State!)) throw new FailedPreconditionException(SchemataResources.FLOW_TOKEN_ASSIGNMENT_INELIGIBLE,
                new Dictionary<string, string?> { ["token"] = tokenName });
            activity = token.WaitingAtName ?? token.StateName;
        }
        var row = new TParticipant {
            Process = process.CanonicalName!, Subject = subject, Kind = kind,
            Token = tokenName, Activity = activity, ExpiresAt = expiresAt?.UtcDateTime,
        };
        await services.GetRequiredService<IResourceMutation<TParticipant>>().CreateAsync(row, uow, ct);
        await uow.CommitAsync(ct);
        return row;
    }

    public async Task RevokeAsync(Guid uid, ClaimsPrincipal principal, CancellationToken ct = default) {
        policy.RequirePermission(FlowAccessPolicy<TProcess, TToken, TParticipant>.Administration, typeof(TProcess), null, principal);
        var participants = services.GetRequiredService<IRepository<TParticipant>>();
        var row = await participants.FirstOrDefaultAsync(q => q.Where(p => p.Uid == uid), ct)
            ?? throw new NotFoundException(SchemataResources.FLOW_PARTICIPANT_NOT_FOUND, new Dictionary<string, string?> { ["uid"] = uid.ToString("D") });
        row.RevokedAt = clock.GetUtcNow().UtcDateTime;
        await services.GetRequiredService<IResourceMutation<TParticipant>>().UpdateAsync(row, null, ct: ct);
    }
}
