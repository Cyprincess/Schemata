using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;

namespace Schemata.Transport.Http;

/// <summary>
///     Schemata trait -> JSON wire-name rewrites shared by every HTTP Schemata
///     surface. Hides <see cref="ICanonicalName.Name" />, surfaces
///     <see cref="ICanonicalName.CanonicalName" /> as <c>name</c> (AIP-122),
///     <see cref="IFreshness.EntityTag" /> as <c>etag</c> (AIP-154), and the
///     <c>Entities</c> property of <see cref="IEntitiesResult{TItem}" /> implementors
///     as the entity plural resolved through
///     <see cref="ResourceNameDescriptor" /> (AIP-132 / AIP-231..235).
/// </summary>
internal static class SchemataJsonTraits
{
    /// <summary>
    ///     Installs the trait modifier on the supplied serializer options.
    /// </summary>
    /// <param name="options">The JSON serializer options to update.</param>
    public static void Apply(JsonSerializerOptions options) {
        options.TypeInfoResolver = (options.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver()).WithAddedModifier(info => {
            foreach (var property in info.Properties.ToArray()) {
                if (property.AttributeProvider is not MemberInfo member) {
                    continue;
                }

                var name = ResourceWireNameRules.ResolveWireName(info.Type, member.Name);
                if (name is null) {
                    info.Properties.Remove(property);
                } else if (name != member.Name) {
                    property.Name = options.PropertyNamingPolicy is not null
                        ? options.PropertyNamingPolicy.ConvertName(name)
                        : name;
                }
            }
        });
    }
}
