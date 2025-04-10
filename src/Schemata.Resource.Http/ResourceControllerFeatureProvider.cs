using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Humanizer;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Primitives;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Http;

public sealed class ResourceControllerFeatureProvider : IApplicationFeatureProvider<ControllerFeature>,
                                                        IActionDescriptorChangeProvider
{
    private CancellationTokenSource                          _cts = new();
    public  Dictionary<RuntimeTypeHandle, ResourceAttribute> Resources { get; set; } = [];

    #region IActionDescriptorChangeProvider Members

    public IChangeToken GetChangeToken() {
        return new CancellationChangeToken(_cts.Token);
    }

    #endregion

    #region IApplicationFeatureProvider<ControllerFeature> Members

    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) {
        foreach (var (_, resource) in Resources) {
            if (resource.Endpoints.Count != 0 && resource.Endpoints.All(e => e.Name != "HTTP")) {
                continue;
            }

            var name = resource.Entity.Name.Pluralize();
            if (feature.Controllers.Any(t => t.Name == name || t.Name == $"{resource.Entity.Name}Controller")) {
                continue;
            }

            var controller = typeof(ResourceController<,,,>)
                            .MakeGenericType(resource.Entity, resource.Request!, resource.Detail!, resource.Summary!)
                            .GetTypeInfo();

            feature.Controllers.Add(controller);
        }
    }

    #endregion

    public void Commit() {
        var old = _cts;
        _cts = new();
        old.Cancel();
    }
}
