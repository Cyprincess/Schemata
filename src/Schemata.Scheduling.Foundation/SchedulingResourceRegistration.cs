using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Foundation;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Scheduling.Foundation;

internal static class SchedulingResourceRegistration
{
    internal static void RegisterHandlers(IServiceCollection services) {
        services.TryAddScoped<RunJobHandler>();
        services.TryAddScoped<CancelOperationHandler>();
        services.TryAddScoped<WaitOperationHandler>();
        services.Map<SchemataJobExecution, Operation>(map => map.With(e => OperationMapper.FromExecution(e)));
    }

    internal static void RegisterMethods(SchemataResourceBuilder resources, string endpointName) {
        resources.Use<SchemataJob, SchemataJob, SchemataJob, SchemataJob>(
            [endpointName],
            resource => resource.Methods = [
                new(Verbs.Run, typeof(RunJobHandler)),
            ]);

        resources.Use<SchemataJobExecution, Operation, Operation, Operation>(
            [endpointName],
            resource => {
                resource.Operations = [Operations.Get, Operations.List, Operations.Delete];
                resource.Methods = [
                    new(Verbs.Cancel, typeof(CancelOperationHandler)),
                    new(Verbs.Wait,   typeof(WaitOperationHandler)),
                ];
            });
    }
}
