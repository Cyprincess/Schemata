using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Expressions.Skeleton;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>
///     A condition expression compiled from source and evaluated against the process variables
///     through a configured expression language. The engine supplies the service provider through
///     <see cref="FlowConditionContext.Services" />.
/// </summary>
public sealed class ExpressionConditionExpression : IConditionExpression
{
    /// <summary>Creates a condition from source text and an optional language override.</summary>
    /// <param name="source">The condition source expression.</param>
    /// <param name="language">The expression language, or null for the process or module default.</param>
    public ExpressionConditionExpression(string source, string? language = null) {
        Source   = source;
        Language = language;
    }

    /// <summary>Gets the condition source expression.</summary>
    public string Source { get; }

    /// <summary>Gets the requested expression language, or null for the process or module default.</summary>
    public string? Language { get; }

    #region IConditionExpression Members

    public ValueTask<bool> Evaluate(FlowConditionContext context) {
        var services = context.Services
                    ?? throw new InvalidOperationException(
                           "ExpressionConditionExpression requires the engine to supply a service provider.");

        var options = services.GetService<IOptions<SchemataFlowOptions>>()?.Value;
        var profile = options?.Expressions ?? new ExpressionLanguageProfile();

        var resolved = ExpressionLanguageResolver.Resolve(profile, Language ?? ProcessLanguage(options, context),
                                                          n => services.GetKeyedService<ExpressionLanguageDescriptor>(n));

        var compiler  = services.GetRequiredKeyedService<IExpressionCompiler>(resolved.Language);
        var tree      = compiler.Parse(Source);
        var predicate = compiler.Compile<IReadOnlyDictionary<string, object?>, bool>(tree).Compile();

        return new(predicate(Normalize(context.Variables)));
    }

    #endregion

    private static string? ProcessLanguage(SchemataFlowOptions? options, FlowConditionContext context) {
        var name = context.Definition?.GetType().Name;
        if (options is null || name is null) {
            return null;
        }

        return options.Configurations.FirstOrDefault(c => c.Name == name)?.Language;
    }

    // Flow variables round-trip through JSON, so values arrive as JsonElement. Dynamic evaluation
    // works against CLR primitives and string-keyed maps, so unwrap them here.
    private static IReadOnlyDictionary<string, object?> Normalize(IReadOnlyDictionary<string, object?> variables) {
        var result = new Dictionary<string, object?>(variables.Count);
        foreach (var kv in variables) {
            result[kv.Key] = Unwrap(kv.Value);
        }

        return result;
    }

    private static object? Unwrap(object? value) {
        if (value is not JsonElement element) {
            return value;
        }

        switch (element.ValueKind) {
            case JsonValueKind.Object:
                var map = new Dictionary<string, object?>();
                foreach (var property in element.EnumerateObject()) {
                    map[property.Name] = Unwrap(property.Value);
                }

                return map;
            case JsonValueKind.Array:
                return element.EnumerateArray().Select(item => Unwrap(item)).ToList();
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                return element.TryGetInt64(out var number) ? number : element.GetDouble();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            default:
                return null;
        }
    }
}
