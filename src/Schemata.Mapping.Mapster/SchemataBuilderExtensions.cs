using Schemata.Core;
using Schemata.Mapping.Foundation;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataBuilderExtensions
{
    public static SchemataMappingBuilder UseMapster(this SchemataBuilder builder) {
        return builder.UseMapping()
                      .UseMapster();
    }
}
