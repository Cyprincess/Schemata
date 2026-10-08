namespace Schemata.Authorization.Skeleton.Services;

public sealed record LogoutSessionTarget(string? Subject, string? SessionId, string Application);
