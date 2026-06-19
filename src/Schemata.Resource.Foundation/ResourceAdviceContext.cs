using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Resource.Foundation.Advisors;

namespace Schemata.Resource.Foundation;

/// <summary>
///     Creates advisor contexts populated with resource-wide suppression markers from options.
/// </summary>
internal static class ResourceAdviceContext
{
    /// <summary>
    ///     Builds an <see cref="AdviceContext" /> for a resource operation.
    /// </summary>
    /// <param name="sp">The service provider for resolving resource options.</param>
    /// <returns>The advisor context carrying configured suppression markers.</returns>
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
