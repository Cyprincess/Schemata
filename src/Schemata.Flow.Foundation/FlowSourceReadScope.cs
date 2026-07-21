using System;
using Schemata.Entity.Owner.Advisors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

namespace Schemata.Flow.Foundation;

/// <summary>
///     Suppresses owner and soft-delete query filters while a flow reads an already-bound source
///     entity: a process holds a durable binding, so its transitions keep access to the source.
/// </summary>
internal static class FlowSourceReadScope
{
    internal static IDisposable Enter(IRepository repository) {
        var owner      = repository.AdviceContext.Use<QueryOwnerSuppressed>();
        var softDelete = repository.AdviceContext.Use<QuerySoftDeleteSuppressed>();
        return new Scope(owner, softDelete);
    }

    private sealed class Scope(IDisposable owner, IDisposable softDelete) : IDisposable
    {
        private bool _disposed;

        public void Dispose() {
            if (_disposed) {
                return;
            }

            _disposed = true;
            softDelete.Dispose();
            owner.Dispose();
        }
    }
}
