using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Resource.Foundation.Advisors;

namespace Schemata.Resource.Foundation;

internal static class ResourceAdviceContext
{
    public static AdviceContext Create(IServiceProvider sp) {
        var ctx = new AdviceContext(sp);

        var options = sp.GetService<IOptions<SchemataResourceOptions>>()?.Value;
        if (options is null) {
            return ctx;
        }

        if (options.SuppressCreateValidation) {
            ctx.Set<CreateRequestValidationSuppressed>(null);
        }

        if (options.SuppressUpdateValidation) {
            ctx.Set<UpdateRequestValidationSuppressed>(null);
        }

        if (options.SuppressFreshness) {
            ctx.Set<FreshnessSuppressed>(null);
        }

        return ctx;
    }
}
