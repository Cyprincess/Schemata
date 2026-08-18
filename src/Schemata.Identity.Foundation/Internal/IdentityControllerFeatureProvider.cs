using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace Schemata.Identity.Foundation.Internal;

/// <summary>
///     Adds one controller type to the MVC controller feature, so a generic Identity controller
///     closed over the application's user type is discovered without assembly scanning.
/// </summary>
/// <param name="controllerType">The closed controller type to expose.</param>
internal sealed class IdentityControllerFeatureProvider(Type controllerType)
    : IApplicationFeatureProvider<ControllerFeature>
{
    #region IApplicationFeatureProvider<ControllerFeature> Members

    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) {
        var typeInfo = controllerType.GetTypeInfo();
        if (!feature.Controllers.Contains(typeInfo)) {
            feature.Controllers.Add(typeInfo);
        }
    }

    #endregion
}
