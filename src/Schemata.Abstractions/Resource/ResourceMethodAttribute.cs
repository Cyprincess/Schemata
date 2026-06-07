using System;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Declares an AIP-136 custom method on a resource class. Multiple
///     attributes can be applied to a single resource to expose several verbs.
///     The <see cref="Verb" /> is rendered as the <c>:{verb}</c> suffix on the
///     HTTP path and as <c>{Verb}{Singular}</c> on the gRPC RPC name within
///     the resource's existing service.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class ResourceMethodAttribute : Attribute
{
    /// <summary>
    ///     Declares an AIP-136 custom method on the annotated resource class.
    /// </summary>
    /// <param name="verb">
    ///     The verb in lowerCamelCase (for example <c>"run"</c>, <c>"archive"</c>,
    ///     <c>"batchCreate"</c>, <c>"signDocument"</c>). Used verbatim as the
    ///     HTTP suffix and PascalCased for the gRPC RPC name.
    /// </param>
    /// <param name="handler">
    ///     A concrete type implementing
    ///     <see cref="IResourceMethodHandler{TEntity, TRequest, TResponse}" />.
    ///     The handler is resolved from DI at invocation time.
    /// </param>
    /// <param name="scope">
    ///     Whether the method targets a single resource instance or the entire
    ///     collection. Defaults to <see cref="ResourceMethodScope.Instance" />.
    /// </param>
    public ResourceMethodAttribute(
        string              verb,
        Type                handler,
        ResourceMethodScope scope = ResourceMethodScope.Instance
    ) {
        Verb    = verb;
        Handler = handler;
        Scope   = scope;
    }

    /// <summary>
    ///     The verb in lowerCamelCase form.
    /// </summary>
    public string Verb { get; }

    /// <summary>
    ///     The handler type implementing
    ///     <see cref="IResourceMethodHandler{TEntity, TRequest, TResponse}" />.
    /// </summary>
    public Type Handler { get; }

    /// <summary>
    ///     Whether the method binds to a single resource instance or to the
    ///     collection.
    /// </summary>
    public ResourceMethodScope Scope { get; }
}
