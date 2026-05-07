using Schemata.Entity.EntityFrameworkCore;

// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

#if NET8_0_OR_GREATER

public static class ModelConfigurationBuilderExtensions
{
    public static ModelConfigurationBuilder UseTableKeyConventions(this ModelConfigurationBuilder builder) {
        builder.Conventions.Add(_ => new TableKeyConvention());
        return builder;
    }
}

#endif
