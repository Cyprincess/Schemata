using Schemata.Core;
using Schemata.Modular;
using Schemata.Modular.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataBuilderExtensions
{
    public static SchemataBuilder UseModular(this SchemataBuilder builder) {
        return UseModular<DefaultModulesRunner, DefaultModulesProvider>(builder);
    }

    public static SchemataBuilder UseModular<TRunner>(this SchemataBuilder builder)
        where TRunner : class, IModulesRunner {
        return UseModular<TRunner, DefaultModulesProvider>(builder);
    }

    public static SchemataBuilder UseModular<TRunner, TProvider>(this SchemataBuilder builder)
        where TProvider : class, IModulesProvider
        where TRunner : class, IModulesRunner {
        builder.AddFeature<SchemataModulesFeature<TProvider, TRunner>>();
        return builder;
    }
}
