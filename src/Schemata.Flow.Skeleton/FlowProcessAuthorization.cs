using System.Linq;
using System.Security.Claims;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Skeleton;

/// <summary>Authorization checks shared by flow resource method handlers.</summary>
internal static class FlowProcessAuthorization
{
    /// <summary>Verifies access to a registered process definition.</summary>
    public static void EnsureDefinitionAccess(
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
    public static void EnsureProcessAccess(
        IProcessRegistry registry,
        SchemataProcess  process,
        ClaimsPrincipal? principal
    ) {
        EnsureDefinitionAccess(registry, process.DefinitionName, principal);
    }

    /// <summary>Verifies access to signal delivery when any listening process requires authorization.</summary>
    public static void EnsureSignalAccess(
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

    private static void EnsureAuthenticated(ClaimsPrincipal? principal) {
        if (principal?.Identity?.IsAuthenticated is true) {
            return;
        }

        throw new NotFoundException();
    }
}
