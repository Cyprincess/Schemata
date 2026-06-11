using System.Linq;
using System.Security.Claims;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Skeleton;

internal static class FlowProcessAuthorization
{
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

    public static void EnsureProcessAccess(
        IProcessRegistry registry,
        SchemataProcess  process,
        ClaimsPrincipal? principal
    ) {
        EnsureDefinitionAccess(registry, process.DefinitionName, principal);
    }

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
