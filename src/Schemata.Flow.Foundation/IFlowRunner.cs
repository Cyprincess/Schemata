using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;
using Schemata.Flow.Skeleton.Entities;

namespace Schemata.Flow.Foundation;

/// <summary>Programmatic entry point for starting registered Flow processes.</summary>
public interface IFlowRunner
{
    /// <summary>Starts a registered process definition and binds a source entity to the new instance.</summary>
    ValueTask<SchemataProcess> StartAsync<TState>(
        string               definitionName,
        TState               source,
        StartProcessOptions? options = null,
        CancellationToken    ct      = default
    ) where TState : class, ICanonicalName;

    /// <summary>Starts a registered process definition without binding a source entity.</summary>
    ValueTask<SchemataProcess> StartAsync(
        string               definitionName,
        StartProcessOptions? options = null,
        CancellationToken    ct      = default
    );
}
