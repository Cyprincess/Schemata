using System;
using Schemata.Expressions.Skeleton;

namespace Schemata.Expressions.Cel;

/// <summary>
///     Enables the CEL expression language on a module builder.
/// </summary>
public static class ExpressionLanguageBuilderExtensions
{
    /// <summary>
    ///     Registers the CEL language services and enables it on the module, appending it in
    ///     priority order behind any languages already enabled.
    /// </summary>
    /// <typeparam name="T">The module builder type.</typeparam>
    /// <param name="builder">The module builder.</param>
    /// <param name="configure">Configures the module's overrides for the language.</param>
    /// <returns>The builder for chaining.</returns>
    public static T UseCel<T>(this T builder, Action<ExpressionLanguageEntry>? configure = null)
        where T : IExpressionLanguageBuilder {
        builder.Services.AddCelExpressions();
        var entry = builder.Languages.Enable(ExpressionLanguages.Cel);
        configure?.Invoke(entry);
        return builder;
    }
}
