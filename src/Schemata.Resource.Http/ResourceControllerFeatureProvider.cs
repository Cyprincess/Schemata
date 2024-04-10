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

public class ResourceControllerFeatureProvider : IApplicationFeatureProvider<ControllerFeature>,
                                                 IActionDescriptorChangeProvider
{
    private CancellationTokenSource             _cts = new();
    public  Dictionary<Type, ResourceAttribute> Resources { get; set; } = [];

    #region IActionDescriptorChangeProvider Members

    public IChangeToken GetChangeToken() {
        return new CancellationChangeToken(_cts.Token);
    }

    #endregion

    #region IApplicationFeatureProvider<ControllerFeature> Members

    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) {
        foreach (var (entity, resource) in Resources) {
            if (resource.Endpoints.Count != 0 && resource.Endpoints.All(e => e.Name != "HTTP")) {
                continue;
            }

            var name = entity.Name.Pluralize();
            if (feature.Controllers.Any(t => t.Name == name || t.Name == $"{entity.Name}Controller")) {
                continue;
            }

            var controller = typeof(ResourceController<,,,>)
                            .MakeGenericType(entity, resource.RequestType!, resource.DetailType!, resource.SummaryType!)
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
