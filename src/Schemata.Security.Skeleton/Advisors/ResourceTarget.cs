using System;

namespace Schemata.Security.Skeleton.Advisors;

/// <summary>
///     The resource a secured request actually addresses: the operation being requested, the
///     entity type it targets, the named instance when the operation addresses one, and the
///     collection parent when the operation addresses a member of a parent collection, per
///     <seealso href="https://google.aip.dev/211">AIP-211: Disallowed Fields</seealso> denial
///     semantics.
/// </summary>
/// <remarks>
///     <para>
///         A collection operation (List, Create) carries no <see cref="Name" /> — the resource
///         does not exist yet or is not individually addressed — and fabricating one would leak
///         existence. An addressed operation (Get, Update, Delete, instance custom methods)
///         carries the requested name so denial facts name what the caller actually asked for.
///     </para>
///     <para>
///         Resolvers build this from the request's own typed fields — never from transport URLs,
///         reflection over envelopes, or a request impersonating its entity.
///     </para>
/// </remarks>
public sealed record ResourceTarget
{
    /// <summary>The operation or custom-method verb being requested.</summary>
    public required string Operation { get; init; }

    /// <summary>The entity type the operation targets; null when the resolver declines.</summary>
    public Type? Entity { get; init; }

    /// <summary>The requested instance name; null for collection operations.</summary>
    public string? Name { get; init; }

    /// <summary>The applicable collection parent; null when the operation addresses the root.</summary>
    public string? Parent { get; init; }

    /// <summary>Builds a collection target: the operation and entity with no instance name.</summary>
    public static ResourceTarget Collection(string operation, Type? entity, string? parent = null) {
        return new() { Operation = operation, Entity = entity, Parent = parent };
    }

    /// <summary>Builds an addressed target naming the requested instance.</summary>
    public static ResourceTarget Instance(string operation, Type? entity, string? name, string? parent = null) {
        return new() { Operation = operation, Entity = entity, Name = name, Parent = parent };
    }
}
