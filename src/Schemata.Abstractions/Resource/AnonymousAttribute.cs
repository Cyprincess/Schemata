using System;
using Schemata.Abstractions.Entities;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Marks a resource as allowing unauthenticated access on the specified
///     operations. When <see cref="Operations" /> is <see langword="null" />,
///     all operations on the resource are anonymous.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AnonymousAttribute : Attribute
{
    /// <summary>
    ///     Allows anonymous access on the given <see cref="Entities.Operations" /> values.
    ///     An empty set means all operations are anonymous.
    /// </summary>
    /// <param name="operations">The CRUD operations to allow anonymously.</param>
    public AnonymousAttribute(params Operations[] operations) {
        Operations = operations.Length > 0
            ? Array.ConvertAll(operations, op => op.ToString())
            : null;
    }

    /// <summary>
    ///     Allows anonymous access for named operation identifiers (e.g. state-machine
    ///     event names) that are not part of the standard <see cref="Entities.Operations" /> enum.
    /// </summary>
    /// <param name="first">The first operation name.</param>
    /// <param name="rest">Additional operation names.</param>
    public AnonymousAttribute(string first, params string[] rest) {
        var ops = new string[1 + rest.Length];
        ops[0] = first;
        rest.CopyTo(ops, 1);
        Operations = ops;
    }

    /// <summary>
    ///     <see langword="null" /> means all operations are anonymous.
    ///     Otherwise, only the listed operation names bypass authentication.
    /// </summary>
    public string[]? Operations { get; }
}
