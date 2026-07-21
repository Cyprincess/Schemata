namespace Schemata.Flow.Skeleton.Models;

/// <summary>Identifies a compensation handler registered for a process scope.</summary>
public sealed record ProcessCompensationBinding(string ScopeOwnerCanonicalName, string ActivityName, int RegistrationOrder);
