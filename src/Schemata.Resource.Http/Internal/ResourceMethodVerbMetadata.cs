namespace Schemata.Resource.Http.Internal;

/// <summary>
///     Endpoint metadata carrying the AIP-136 verb bound to a generated custom-method action. A
///     single closed <c>ResourceMethodController</c> hosts one action per verb when a handler is
///     registered for several verbs, and the action reads its verb from this metadata at runtime.
/// </summary>
/// <param name="Verb">The lowerCamelCase custom-method verb.</param>
internal sealed record ResourceMethodVerbMetadata(string Verb);
