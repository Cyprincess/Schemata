using System.Linq;
using System.Security.Claims;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Foundation;

/// <summary>
///     Authorization checks shared by flow resource method handlers. Concrete class with virtual
///     methods so consumers can subclass and replace per-process / per-signal access rules without
///     introducing a new interface contract. Registered as a singleton in DI;
///     <see cref="FlowRunner" /> depends on this concrete type.
/// </summary>
public class FlowProcessAuthorization
{
    /// <summary>Verifies access to a registered process definition.</summary>
    public virtual void EnsureDefinitionAccess(
        IProcessRegistry registry,
        string           definitionName,
        ClaimsPrincipal? principal
    ) {
        var registration = registry.GetRegistration(definitionName);
        if (registration?.Configuration.RequiresAuthorization is true) {
            EnsureAuthenticated(principal);
        }
    }

    /// <summary>Verifies access to a persisted process instance through its source definition.</summary>
    public virtual void EnsureProcessAccess(
        IProcessRegistry registry,
        SchemataProcess  process,
        ClaimsPrincipal? principal
    ) {
        EnsureDefinitionAccess(registry, process.DefinitionName, principal);
    }

    /// <summary>Verifies access to signal delivery when any listening process requires authorization.</summary>
    public virtual void EnsureSignalAccess(
        IProcessRegistry registry,
        string           signalName,
        ClaimsPrincipal? principal
    ) {
        var requires = registry.GetRegisteredProcesses()
                               .Select(registry.GetRegistration)
                               .Where(r => r is not null)
                               .Any(r => r!.Configuration.RequiresAuthorization
                                      && r.Definition.Signals.Any(s => s.Name == signalName));
        if (requires) {
            EnsureAuthenticated(principal);
        }
    }

    /// <summary>Hook for subclasses: throws <see cref="NotFoundException" /> when the principal is anonymous.</summary>
    protected virtual void EnsureAuthenticated(ClaimsPrincipal? principal) {
        if (principal?.Identity?.IsAuthenticated is true) {
            return;
        }

        throw new NotFoundException();
    }
}
