using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;

namespace Schemata.Mapping.Foundation;

public sealed class SchemataMappingBuilder
{
    public SchemataMappingBuilder(SchemataOptions schemata, IServiceCollection services) {
        Schemata = schemata;
        Services = services;
    }

    public SchemataOptions Schemata { get; }

    public IServiceCollection Services { get; }
}
