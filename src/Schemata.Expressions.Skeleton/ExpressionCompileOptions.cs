using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Schemata.Expressions.Skeleton;

/// <summary>
///     Supplies optional bindings used while compiling expression trees.
/// </summary>
public sealed class ExpressionCompileOptions
{
    public ExpressionCompileOptions() { }

    private ExpressionCompileOptions(IDictionary<string, ExpressionFunction> functions, string? contextAlias) {
        Functions    = functions;
        ContextAlias = contextAlias;
    }

    /// <summary>Captures function bindings while preserving application-owned delegate state.</summary>
    public ExpressionCompileOptions Snapshot() {
        return new(new ReadOnlyDictionary<string, ExpressionFunction>(new Dictionary<string, ExpressionFunction>(Functions)), ContextAlias);
    }

    /// <summary>
    ///     Gets custom functions available to expression compilers by name.
    /// </summary>
    public IDictionary<string, ExpressionFunction> Functions { get; } = new Dictionary<string, ExpressionFunction>();

    /// <summary>
    ///     Binds a leading identifier (for example an Insight source alias) to the context
    ///     parameter, so a qualified path such as <c>s.age</c> resolves against the row instead of
    ///     a member named <c>s</c>. <see langword="null" /> disables alias binding.
    /// </summary>
    public string? ContextAlias { get; set; }

    /// <summary>
    ///     Creates a cache-key fragment encoding the built-in function version, the context alias
    ///     binding, and any custom function bindings so two option sets that bind the same name to
    ///     different delegates do not share a cached result.
    /// </summary>
    /// <param name="options">The compile options, or <see langword="null" /> when none are supplied.</param>
    /// <param name="builtinsVersion">Language-specific version tag for the built-in function set.</param>
    public static string Fingerprint(ExpressionCompileOptions? options, string builtinsVersion = "v1") {
        var alias = options?.ContextAlias is { } contextAlias ? $"alias:{contextAlias}" : "alias:none";
        if (options is null || options.Functions.Count == 0) {
            return $"builtins:{builtinsVersion};{alias};functions:none";
        }

        return $"builtins:{builtinsVersion};{alias};functions:" + string.Join(
            ",",
            options.Functions.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                   .Select(kv => $"{kv.Key}:{RuntimeHelpers.GetHashCode(kv.Value)}"));
    }
}
