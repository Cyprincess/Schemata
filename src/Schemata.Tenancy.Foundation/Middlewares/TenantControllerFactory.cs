using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Schemata.Core;
using Schemata.Tenancy.Skeleton.Tenancy;

namespace Schemata.Tenancy.Foundation.Middlewares;

internal sealed class TenantControllerFactory(IControllerFactory inner) : IControllerFactory
{
    public object CreateController(ControllerContext context) {
        using var frame = TenantContext.Enter(context.HttpContext.Features.Get<ITenantRequestBinding>()?.Identity ?? TenantContext.Current);
        return inner.CreateController(context);
    }

    public void ReleaseController(ControllerContext context, object controller) {
        using var frame = TenantContext.Enter(context.HttpContext.Features.Get<ITenantRequestBinding>()?.Identity ?? TenantContext.Current);
        inner.ReleaseController(context, controller);
    }

    public async ValueTask ReleaseControllerAsync(ControllerContext context, object controller) {
        using var frame = TenantContext.Enter(context.HttpContext.Features.Get<ITenantRequestBinding>()?.Identity ?? TenantContext.Current);
        await inner.ReleaseControllerAsync(context, controller);
    }
}

/// <summary>Composes <see cref="TenantControllerFactory" /> over the framework controller factory.</summary>
internal sealed class TenantControllerFactoryDecorator : IDecorator<IControllerFactory>
{
    public IControllerFactory Decorate(IControllerFactory inner) { return new TenantControllerFactory(inner); }
}
