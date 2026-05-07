#if NET8_0_OR_GREATER

using System.Linq;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Schemata.Entity.Repository;

namespace Schemata.Entity.EntityFrameworkCore;

public class TableKeyConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            var clrType = entityType.ClrType;
            if (clrType is null || entityType.FindPrimaryKey() is not null)
            {
                continue;
            }

            var keyProps = clrType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .SelectMany(p => p.GetCustomAttributes<TableKeyAttribute>(true)
                    .Select(a => (Property: p, Order: a.Order)))
                .OrderBy(x => x.Order)
                .Select(x => x.Property.Name)
                .ToList();

            if (keyProps.Count > 0)
            {
                entityType.Builder.PrimaryKey(keyProps);
            }
        }
    }
}

#endif
