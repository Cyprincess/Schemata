using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Schemata.Expressions.Skeleton;

/// <summary>
///     Supplies optional bindings used while compiling expression trees.
/// </summary>
public sealed class ExpressionCompileOptions
{
    /// <summary>
    ///     Gets custom functions available to expression compilers by name.
    /// </summary>
    public IDictionary<string, ExpressionFunction> Functions { get; } = new Dictionary<string, ExpressionFunction>();

    /// <summary>
    ///     Creates a cache-key fragment encoding the built-in function version and any custom
    ///     function bindings so two option sets that bind the same name to different delegates
    ///     do not share a cached result.
    /// </summary>
    /// <param name="options">The compile options, or <see langword="null" /> when none are supplied.</param>
    /// <param name="builtinsVersion">Language-specific version tag for the built-in function set.</param>
    public static string Fingerprint(ExpressionCompileOptions? options, string builtinsVersion = "v1") {
        if (options is null || options.Functions.Count == 0) {
            return $"builtins:{builtinsVersion};functions:none";
        }

        return $"builtins:{builtinsVersion};functions:" + string.Join(
            ",",
            options.Functions.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                   .Select(kv => $"{kv.Key}:{RuntimeHelpers.GetHashCode(kv.Value)}"));
    }
}
