using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Transport.Http;

/// <summary>
///     Schemata trait → JSON wire-name rewrites shared by every HTTP Schemata
///     surface. Hides <see cref="ICanonicalName.Name" />, surfaces
///     <see cref="ICanonicalName.CanonicalName" /> as <c>name</c> (AIP-122),
///     <see cref="IFreshness.EntityTag" /> as <c>etag</c> (AIP-154), and
///     <see cref="ListResultBase{TSummary}.Entities" /> as the entity plural resolved
///     through <see cref="ResourceNameDescriptor" /> (AIP-132).
/// </summary>
internal static class SchemataJsonTraits
{
    /// <summary>Installs the trait modifier on top of <paramref name="options" />.</summary>
    public static void Apply(JsonSerializerOptions options) {
        options.TypeInfoResolver = options.TypeInfoResolver?.WithAddedModifier(info => {
            if (typeof(ICanonicalName).IsAssignableFrom(info.Type)) {
                var np = info.Properties.FirstOrDefault(p => p.AttributeProvider is MemberInfo {
                    Name: nameof(ICanonicalName.Name),
                });
                if (np is not null) {
                    info.Properties.Remove(np);
                }

                var cp = info.Properties.FirstOrDefault(p => p.AttributeProvider is MemberInfo {
                    Name: nameof(ICanonicalName.CanonicalName),
                });
                cp?.Name = Parameters.Name;
            }

            if (typeof(IFreshness).IsAssignableFrom(info.Type)) {
                var ep = info.Properties.FirstOrDefault(p => p.AttributeProvider is MemberInfo {
                    Name: nameof(IFreshness.EntityTag),
                });

                ep?.Name = Parameters.EntityTag;
            }

            if (info.Type is { IsGenericType: true }
             && info.Type.GetGenericTypeDefinition() == typeof(ListResultBase<>)) {
                var property = info.Properties.FirstOrDefault(p => p.AttributeProvider is MemberInfo {
                    Name: nameof(ListResultBase<>.Entities),
                });

                if (property is not null) {
                    var summary = info.Type.GetGenericArguments()[0];
                    var plural  = ResourceNameDescriptor.ForType(summary).Plural;
                    property.Name = options.PropertyNamingPolicy is not null
                        ? options.PropertyNamingPolicy.ConvertName(plural)
                        : plural;
                }
            }
        });
    }
}
