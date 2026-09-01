using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Resource.Foundation.Advisors;

namespace Schemata.Resource.Foundation;

/// <summary>
///     Continues the ambient <see cref="AdviceContext" /> for a resource operation, seeding
///     resource-wide suppression markers from options.
/// </summary>
internal static class ResourceAdviceContext
{
    /// <summary>
    ///     Continues the ambient <see cref="AdviceContext" /> established by the pipeline root
    ///     (<c>IRequestDispatcher</c>) for a resource operation, seeding configured suppression
    ///     markers into it.
    /// </summary>
    /// <param name="sp">The service provider for resolving resource options.</param>
    /// <returns>The ambient advisor context carrying configured suppression markers.</returns>
    /// <exception cref="InvalidOperationException">
    ///     No ambient <see cref="AdviceContext" /> is established; the resource pipeline must be
    ///     entered through <c>IRequestDispatcher</c> rather than invoked directly.
    /// </exception>
    public static AdviceContext Create(IServiceProvider sp) {
        var ctx = AdviceContext.Require();

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
