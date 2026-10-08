using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Tenancy;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Foundation;

public sealed class FlowParticipantManager(
    IServiceProvider services,
    FlowAccessPolicy policy,
    TimeProvider clock
)
{
    public async Task<SchemataProcessParticipant> GrantAsync(
        string processName, string subject, ProcessParticipationKind kind, ClaimsPrincipal principal,
        string? tokenName = null, DateTimeOffset? expiresAt = null, CancellationToken ct = default) {
        policy.RequirePermission(FlowAccessPolicy.Administration, typeof(SchemataProcess), processName, principal);
        if (string.IsNullOrWhiteSpace(subject) || !Enum.IsDefined(kind)) throw new InvalidArgumentException(SchemataResources.FLOW_PARTICIPANT_INVALID);
        if (kind == ProcessParticipationKind.Participation && tokenName is not null) throw new InvalidArgumentException(SchemataResources.FLOW_PARTICIPATION_PROCESS_ONLY);
        var tenant = TenantContext.Current.Uid;
        var processes = services.GetRequiredService<IRepository<SchemataProcess>>();
        var participants = services.GetRequiredService<IRepository<SchemataProcessParticipant>>();
        var tokens = services.GetRequiredService<IRepository<SchemataProcessToken>>();
        await using var uow = processes.Begin();
        participants.Join(uow);
        tokens.Join(uow);
        var process = await processes.FirstOrDefaultAsync(q => q.Where(p => p.CanonicalName == processName && p.TenantUid == tenant), ct)
            ?? throw new NotFoundException(SchemataResources.FLOW_PROCESS_NOT_FOUND, new Dictionary<string, string?> { ["name"] = processName });
        string? activity = null;
        if (kind == ProcessParticipationKind.Eligibility && tokenName is not null) {
            var token = await tokens.FirstOrDefaultAsync(q => q.Where(t => t.CanonicalName == tokenName && t.Process == process.Name && t.TenantUid == tenant), ct);
            if (token is null || !TokenStates.Live.Contains(token.State!)) throw new FailedPreconditionException(SchemataResources.FLOW_TOKEN_ASSIGNMENT_INELIGIBLE,
                new Dictionary<string, string?> { ["token"] = tokenName });
            activity = token.WaitingAtName ?? token.StateName;
        }
        var row = new SchemataProcessParticipant {
            Process = process.CanonicalName!, Subject = subject, Kind = kind, TenantUid = tenant,
            Token = tokenName, Activity = activity, ExpiresAt = expiresAt?.UtcDateTime,
        };
        await services.GetRequiredService<IResourceMutation<SchemataProcessParticipant>>().CreateAsync(row, uow, ct);
        await uow.CommitAsync(ct);
        return row;
    }

    public async Task RevokeAsync(Guid uid, ClaimsPrincipal principal, CancellationToken ct = default) {
        policy.RequirePermission(FlowAccessPolicy.Administration, typeof(SchemataProcess), null, principal);
        var tenant = TenantContext.Current.Uid;
        var participants = services.GetRequiredService<IRepository<SchemataProcessParticipant>>();
        var row = await participants.FirstOrDefaultAsync(q => q.Where(p => p.Uid == uid && p.TenantUid == tenant), ct)
            ?? throw new NotFoundException(SchemataResources.FLOW_PARTICIPANT_NOT_FOUND, new Dictionary<string, string?> { ["uid"] = uid.ToString("D") });
        row.RevokedAt = clock.GetUtcNow().UtcDateTime;
        await services.GetRequiredService<IResourceMutation<SchemataProcessParticipant>>().UpdateAsync(row, null, ct: ct);
    }
}
