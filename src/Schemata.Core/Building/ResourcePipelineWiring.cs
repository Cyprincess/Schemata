using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Core.Building;

public sealed record ResourcePipelineWiring(
    Func<ResourceRegistration, IReadOnlyList<ResourceMethodRegistration>> PrepareMethods,
    Action<IServiceCollection, ResourceRegistration> RegisterResource,
    Func<IServiceCollection, ResourceRegistration, ResourceMethodRegistration, IReadOnlyList<ServiceDescriptor>> RegisterMethod,
    Func<ResourceRegistration, ResourceMethodRegistration, (Type Request, Type Response)> MethodClosure,
    Action<IServiceCollection, ResourceRegistration, IReadOnlyList<ResourceMethodRegistration>> RegisterAuthentication,
    Action<IServiceCollection, ResourceRegistration, IReadOnlyList<ResourceMethodRegistration>> RegisterAuthorization,
    Action<IServiceCollection> ApplyStageChoices
);
