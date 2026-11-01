namespace Schemata.Authorization.Skeleton.Services;

public sealed record LogoutSessionEvidence(LogoutSessionTarget Target, bool MatchesCurrent, bool HasRecentEvidence);
