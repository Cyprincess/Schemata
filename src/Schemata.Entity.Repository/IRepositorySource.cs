using System;

namespace Schemata.Entity.Repository;

/// <summary>
///     Exposes the hashed identity of the physical data source a repository or native unit of
///     work executes against. Before a repository adopts a unit of work's context, the provider
///     compares source keys — the fixed <see cref="RepositoryBinding" /> namespace is folded
///     into the hash — so contexts targeting different databases or tenant bindings cannot
///     share a native transaction.
/// </summary>
public interface IRepositorySource
{
    /// <summary>
    ///     Gets the hashed source key. The key is a SHA-256 digest over the provider name, the
    ///     physical data-source fingerprint (or the owning factory's identity for ephemeral
    ///     sources), and the binding namespace; it never contains cleartext credentials.
    /// </summary>
    string SourceKey { get; }
}

/// <summary>
///     Receipt written by a provider's standard source registration entry into the service
///     collection it configured. Registration entry points check this descriptor against the
///     local collection to prove the source factory was configured tenant-side through the
///     standard entry instead of being inherited through host fallback.
/// </summary>
/// <param name="ContextType">The native context type the registered factory creates.</param>
public sealed record RepositorySourceRegistration(Type ContextType);
