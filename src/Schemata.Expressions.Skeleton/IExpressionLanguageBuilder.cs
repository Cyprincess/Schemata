using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Expressions.Skeleton;

/// <summary>
///     A module builder that enables expression languages. Language packages provide
///     <c>Use*</c> extensions over this seam so modules never reference a concrete language package.
/// </summary>
public interface IExpressionLanguageBuilder
{
    /// <summary>
    ///     Gets the service collection that receives language registrations.
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>
    ///     Gets the module's enabled languages in priority order.
    /// </summary>
    ExpressionLanguageProfile Languages { get; }
}
