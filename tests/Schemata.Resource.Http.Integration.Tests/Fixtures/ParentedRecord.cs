using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Http.Integration.Tests.Fixtures;

[CanonicalName("parents/{parent_id}/records/{record}")]
[Microsoft.EntityFrameworkCore.PrimaryKey(nameof(Uid))]
public sealed class ParentedRecord : IIdentifier, ICanonicalName {
    public Guid Uid { get; set; }
    public string? Name { get; set; }
    public string? CanonicalName { get; set; }
    public string? ParentId { get; set; } = "allowed";
}

public sealed class ParentedRecordRequest : ICanonicalName, IChild {
    public string? Name { get; set; }
    public string? CanonicalName { get; set; }
    public string? Parent { get; set; }
}

public sealed class ParentedRecordAccess : IAccessProvider<ParentedRecord, ParentedRecordRequest> {
    public Task<AccessDecision> HasAccessAsync(ParentedRecord? entity, AccessContext<ParentedRecordRequest> context,
        ClaimsPrincipal? principal, CancellationToken ct = default) =>
        Task.FromResult(entity?.ParentId == "allowed" ? AccessDecision.Allowed : AccessDecision.Denied);
}
