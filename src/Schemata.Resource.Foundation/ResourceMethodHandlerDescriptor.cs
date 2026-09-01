using System;

namespace Schemata.Resource.Foundation;

/// <summary>Normalized runtime types for an AIP-136 resource method.</summary>
/// <param name="Entity">The resource entity type.</param>
/// <param name="Request">The method request type.</param>
/// <param name="Response">The method response type.</param>
/// <param name="Handler">The concrete handler type registered behind the closed request-handler interface.</param>
public sealed record ResourceMethodHandlerDescriptor(Type Entity, Type Request, Type Response, Type Handler);
