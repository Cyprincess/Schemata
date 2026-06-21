using System;
using Schemata.Expressions.Skeleton;

namespace Schemata.Expressions.Aip;

/// <summary>
///     Enables the AIP expression language on a module builder.
/// </summary>
public static class ExpressionLanguageBuilderExtensions
{
    /// <summary>
    ///     Registers the AIP language services and enables it on the module, appending it in
    ///     priority order behind any languages already enabled.
    /// </summary>
    /// <typeparam name="T">The module builder type.</typeparam>
    /// <param name="builder">The module builder.</param>
    /// <param name="configure">Configures the module's overrides for the language.</param>
    /// <returns>The builder for chaining.</returns>
    public static T UseAip<T>(this T builder, Action<ExpressionLanguageEntry>? configure = null)
        where T : IExpressionLanguageBuilder {
        builder.Services.AddAipExpressions();
        var entry = builder.Languages.Enable(ExpressionLanguages.Aip);
        configure?.Invoke(entry);
        return builder;
    }
}
