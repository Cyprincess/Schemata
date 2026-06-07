namespace Schemata.Resource.Grpc;

/// <summary>
///     AIP-136 naming utilities for gRPC custom method RPCs on resource services.
/// </summary>
public static class ResourceMethodNaming
{
    /// <summary>
    ///     Computes the gRPC RPC name for an AIP-136 custom method:
    ///     <c>{PascalVerb}{Singular}</c> (for example, <c>RunJob</c>,
    ///     <c>BatchCreateBook</c>, <c>SignDocument</c>).
    /// </summary>
    /// <param name="verb">The verb in lowerCamelCase form as declared by
    ///     <see cref="Schemata.Abstractions.Resource.ResourceMethodAttribute" />.</param>
    /// <param name="singular">The resource's singular form
    ///     (e.g. <c>Job</c>, <c>Book</c>).</param>
    public static string GetRpcName(string verb, string singular) {
        if (string.IsNullOrEmpty(verb)) {
            return singular;
        }
        return $"{char.ToUpperInvariant(verb[0])}{verb[1..]}{singular}";
    }
}
