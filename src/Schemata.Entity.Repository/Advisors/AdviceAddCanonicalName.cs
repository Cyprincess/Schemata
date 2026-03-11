using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;

namespace Schemata.Entity.Repository.Advisors;

public class AdviceAddCanonicalName
{
    protected static readonly Regex ResourceNameRegex = new(@"\{(?<name>\w+)\}");
}

public sealed class AdviceAddCanonicalName<TEntity> : AdviceAddCanonicalName, IRepositoryAddAdvisor<TEntity>
    where TEntity : class
{
    #region IRepositoryAddAdvisor<TEntity> Members

    public int Order => 300_000_000;

    public int Priority => Order;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        CancellationToken    ct
    ) {
        if (entity is not ICanonicalName named) {
            return Task.FromResult(AdviseResult.Continue);
        }

        var type = entity.GetType();

        var attribute = type.GetCustomAttribute<CanonicalNameAttribute>(false);
        if (attribute is null) {
            return Task.FromResult(AdviseResult.Continue);
        }

        var current = type.GetCustomAttribute<DisplayNameAttribute>(false)?.DisplayName.Singularize()
                   ?? type.GetCustomAttribute<TableAttribute>(false)?.Name.Singularize() ?? type.Name;

        var properties = AppDomainTypeCache.GetProperties(type);

        var name = ResourceNameRegex.Replace(attribute.ResourceName, m => {
            var matched = m.Groups["name"].Value.Pascalize();

            var field = matched switch {
                "Parent"                                   => "Parent",
                var _ when string.Equals(current, matched) => "Name",
                var _                                      => $"{matched}Name",
            };

            if (!properties.TryGetValue(field, out var property)) {
                throw new MissingFieldException(type.Name, field);
            }

            var value = property.GetValue(entity)?.ToString();
            if (string.IsNullOrWhiteSpace(value)) {
                throw new ValidationException([new(field, "not_empty")]);
            }

            return value;
        });

        named.CanonicalName = name;

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
