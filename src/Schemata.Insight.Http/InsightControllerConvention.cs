using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Authorization;

namespace Schemata.Insight.Http;

internal sealed class InsightControllerConvention(string scheme) : IControllerModelConvention
{
    public void Apply(ControllerModel controller) {
        if (controller.ControllerType.AsType() != typeof(InsightController)) {
            return;
        }

        var policy = new AuthorizationPolicyBuilder(scheme).RequireAuthenticatedUser().Build();
        controller.Filters.Add(new AuthorizeFilter(policy));
    }
}
