using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Primitives;
using Schemata.Abstractions.Resource;
using Schemata.Common;

namespace Schemata.Resource.Http;

/// <summary>
///     Dynamically adds generic <see cref="ResourceController{TEntity,TRequest,TDetail,TSummary}" /> instances
///     for registered resources and notifies MVC when the controller set changes
///     per <seealso href="https://google.aip.dev/127">AIP-127: HTTP and gRPC Transcoding</seealso>.
/// </summary>
public sealed class ResourceControllerFeatureProvider : IApplicationFeatureProvider<ControllerFeature>,
                                                        IActionDescriptorChangeProvider
{
    private CancellationTokenSource _cts = new();

    /// <summary>
    ///     Gets or sets the registered resources that should produce HTTP controllers.
    /// </summary>
    public Dictionary<RuntimeTypeHandle, ResourceAttribute> Resources { get; set; } = [];

    #region IActionDescriptorChangeProvider Members

    public IChangeToken GetChangeToken() { return new CancellationChangeToken(_cts.Token); }

    #endregion

    #region IApplicationFeatureProvider<ControllerFeature> Members

    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) {
        foreach (var (_, resource) in Resources) {
            // Skip resources that have explicit endpoint declarations but none of them
            // target the HTTP transport — a user-defined gRPC-only resource should not
            // get an auto-generated REST controller.
            if (resource.Endpoints?.Count != 0
             && resource.Endpoints?.All(e => e != HttpResourceAttribute.Name) == true) {
                continue;
            }

            // Avoid duplicates when a user-defined controller already exists for this
            // resource, either by plural name or by entity-name convention.
            var name = ResourceNameDescriptor.ForType(resource.Entity).Plural;
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

    /// <summary>
    ///     Signals MVC that the controller set has changed and action descriptors should be refreshed.
    /// </summary>
    public void Commit() {
        var old = _cts;
        _cts = new();
        old.Cancel();
    }
}
