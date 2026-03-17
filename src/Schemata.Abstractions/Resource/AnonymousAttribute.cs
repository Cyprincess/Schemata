using System;
using Schemata.Abstractions.Entities;

namespace Schemata.Abstractions.Resource;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AnonymousAttribute : Attribute
{
    public AnonymousAttribute(params Operations[] operations) {
        Operations = operations.Length > 0 ? operations : null;
    }

    /// <summary>
    ///     Null = all operations anonymous. Otherwise, only specified operations.
    /// </summary>
    public Operations[]? Operations { get; }
}
