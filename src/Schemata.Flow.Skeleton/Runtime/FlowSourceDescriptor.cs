using System;
using Humanizer;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>Compiled source projection delegates for a process binding.</summary>
public sealed class FlowSourceDescriptor
{
    /// <summary>The source binding name.</summary>
    public required string BindingName { get; init; }

    /// <summary>
    ///     The binding name a source type carries when the declaration site omits one:
    ///     <c>ExpenseClaim</c> binds as <c>expense_claim</c>. Declaration, resolution and projection
    ///     all derive the default name here, so one source type addresses one binding.
    /// </summary>
    /// <param name="source">The source entity type.</param>
    public static string DefaultBindingName(Type source) {
        ArgumentNullException.ThrowIfNull(source);

        return source.Name.Underscore().ToLowerInvariant();
    }

    /// <summary>The binding name <typeparamref name="TSource" /> carries when none is declared.</summary>
    /// <typeparam name="TSource">The source entity type.</typeparam>
    public static string DefaultBindingName<TSource>() { return DefaultBindingName(typeof(TSource)); }

    /// <summary>The bound source entity type.</summary>
    public required Type SourceType { get; init; }

    /// <summary>The resolved source projection mode.</summary>
    public required FlowSourceProjection Projection { get; init; }

    /// <summary>Reads the projected state member.</summary>
    public Func<object, string?>? GetState { get; init; }

    /// <summary>Writes the projected state member.</summary>
    public Action<object, string?>? SetState { get; init; }

    /// <summary>Reads the projected lifecycle member.</summary>
    public Func<object, string?>? GetLifecycle { get; init; }

    /// <summary>Writes the projected lifecycle member.</summary>
    public Action<object, string?>? SetLifecycle { get; init; }
}
