using Microsoft.Extensions.DependencyInjection;
using Schemata.Expressions.Skeleton;

namespace Schemata.Expressions.Cel;

/// <summary>
///     CEL expression language registration extensions.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the CEL filter compiler under <see cref="CelLanguage.Name" />.
    /// </summary>
    public static IServiceCollection AddCelExpressions(this IServiceCollection services) {
        services.AddKeyedSingleton<IExpressionCompiler, CelCompiler>(CelLanguage.Name);
        return services;
    }
}
