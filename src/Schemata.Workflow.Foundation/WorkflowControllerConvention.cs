using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Authorization;
using Schemata.Workflow.Foundation.Controllers;

namespace Schemata.Workflow.Foundation;

internal sealed class WorkflowControllerConvention(string? scheme = null) : IControllerModelConvention
{
    #region IControllerModelConvention Members

    /// <inheritdoc />
    public void Apply(ControllerModel controller) {
        if (controller.ControllerType != typeof(WorkflowController)) return;

        if (!string.IsNullOrWhiteSpace(scheme)) {
            var policy = new AuthorizationPolicyBuilder(scheme).RequireAssertion(_ => true).Build();
            controller.Filters.Add(new AuthorizeFilter(policy));
        }
    }

    #endregion
}
