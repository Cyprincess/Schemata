namespace Schemata.Authorization.Skeleton.Services;

/// <summary>
///     One piece of OP-session evidence read from a participating mechanism: the subject it
///     names (when known), its session identifier, and how the evidence was established.
/// </summary>
public sealed record OpSessionEvidence(string? Subject, string? SessionId, OpSessionProvenance Provenance);