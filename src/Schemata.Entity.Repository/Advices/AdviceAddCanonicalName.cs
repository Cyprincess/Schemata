using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using Schemata.Abstractions.Entities;

namespace Schemata.Entity.Repository.Advices;

public class AdviceAddCanonicalName
{
    protected static readonly Regex ResourceNameRegex = new(@"\{(?<name>\w+)\}");

    protected static readonly Dictionary<Type, Dictionary<string, PropertyInfo>> PropertiesCache = new();
}

public sealed class AdviceAddCanonicalName<TEntity> : AdviceAddCanonicalName, IRepositoryAddAsyncAdvice<TEntity>
    where TEntity : class
{
    #region IRepositoryAddAsyncAdvice<TEntity> Members

    public int Order => 300_000_000;

    public int Priority => Order;

    public Task<bool> AdviseAsync(IRepository<TEntity> repository, TEntity entity, CancellationToken ct) {
        if (entity is not ICanonicalName named) {
            return Task.FromResult(true);
        }

        var type = entity.GetType();

        var attribute = type.GetCustomAttribute<CanonicalNameAttribute>(false);
        if (attribute == null) {
            return Task.FromResult(true);
        }

        var current = type.GetCustomAttribute<TableAttribute>(false)?.Name.Singularize() ?? type.Name;

        if (!PropertiesCache.TryGetValue(type, out var properties)) {
            properties = type
                        .GetProperties(BindingFlags.GetProperty
                                     | BindingFlags.IgnoreCase
                                     | BindingFlags.Public
                                     | BindingFlags.Instance)
                        .ToDictionary(p => p.Name, p => p);

            PropertiesCache.Add(type, properties);
        }

        var name = ResourceNameRegex.Replace(attribute.ResourceName, m => {
            var matched = m.Groups["name"].Value.Pascalize();
            if (string.Equals(current, matched)) {
                matched = string.Empty;
            }

            if (!properties.TryGetValue($"{matched}Name", out var property)) {
                throw new MissingFieldException(type.Name, $"{matched}Name");
            }

            return property.GetValue(entity)?.ToString() ?? string.Empty;
        });

        named.CanonicalName = name;

        return Task.FromResult(true);
    }

    #endregion
}
