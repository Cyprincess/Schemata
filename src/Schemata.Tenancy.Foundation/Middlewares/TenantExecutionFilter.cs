using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Filters;
using Schemata.Abstractions.Tenancy;

namespace Schemata.Tenancy.Foundation.Middlewares;

internal sealed class TenantExecutionFilter : IAsyncResourceFilter, IOrderedFilter
{
    public int Order => int.MinValue;

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next) {
        using var frame = TenantContext.Enter(context.HttpContext.Features.Get<ITenantRequestBinding>()?.Identity ?? TenantContext.Current);
        await next();
    }
}
