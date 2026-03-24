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
    ///     Initializes a new instance of the <see cref="AnonymousAttribute" /> class.
    /// </summary>
    /// <param name="operations">
    ///     The operations that allow anonymous access. If none are specified, all operations are anonymous.
    /// </param>
    public AnonymousAttribute(params Operations[] operations) {
        Operations = operations.Length > 0 ? operations : null;
    }

    /// <summary>
    ///     Null = all operations anonymous. Otherwise, only specified operations.
    /// </summary>
    public Operations[]? Operations { get; }
}
