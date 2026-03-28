using System;
using Schemata.Abstractions.Entities;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Marks a resource as allowing anonymous (unauthenticated) access for all or specific operations.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AnonymousAttribute : Attribute
{
    /// <summary>
    ///     Initializes a new instance allowing anonymous access for the specified CRUD operations.
    ///     If none are specified, all operations are anonymous.
    /// </summary>
    /// <param name="operations">The CRUD operations that allow anonymous access.</param>
    public AnonymousAttribute(params Operations[] operations) {
        Operations = operations.Length > 0
            ? Array.ConvertAll(operations, op => op.ToString())
            : null;
    }

    /// <summary>
    ///     Initializes a new instance allowing anonymous access for the specified named operations.
    ///     Intended for state machine event names and other non-CRUD operation identifiers.
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
    ///     Null = all operations anonymous. Otherwise, only the specified operation names are anonymous.
    /// </summary>
    public string[]? Operations { get; }
}
