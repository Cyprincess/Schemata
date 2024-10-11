using Schemata.Core;
using Schemata.Mapping.Foundation;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataBuilderExtensions
{
    public static SchemataMappingBuilder UseMapping(this SchemataBuilder builder) {
        return new(builder.Options, builder.Services);
    }
}
