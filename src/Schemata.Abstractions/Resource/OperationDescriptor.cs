using System;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Startup-registered binding for a durable operation. Persisted job rows store
///     only the stable <see cref="Key" /> and serialized arguments; the
///     <see cref="ArgsType" /> is resolved from this descriptor at execution time, so
///     no CLR type is ever loaded from persisted data.
/// </summary>
/// <param name="Key">Stable key used to resolve the handler (e.g. <c>purge:books</c>).</param>
/// <param name="Method">API method verb surfaced as operation metadata (e.g. <c>purge</c>).</param>
/// <param name="ArgsType">Concrete argument type used to deserialize the persisted arguments.</param>
public sealed record OperationDescriptor(string Key, string Method, Type ArgsType);
