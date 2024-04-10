using System;
using System.Linq.Expressions;

namespace Schemata.Mapping.Skeleton.Configurations;

public sealed class Mapping<TSource, TDestination> : IMapping
{
    internal Mapping(Map<TSource, TDestination> map, Expression<Func<TDestination, object?>> destinationField) {
        Map              = map;
        DestinationField = destinationField;
    }

    internal Map<TSource, TDestination> Map { get; }

    internal Expression<Func<TDestination, object?>> DestinationField { get; }

    internal Expression<Func<TSource, object?>>? SourceField { get; private set; }

    internal Expression<Func<TSource, TDestination, bool>>? IgnoreCondition { get; private set; }

    #region IMapping Members

    public Type SourceType { get; } = typeof(TSource);

    public Type DestinationType { get; } = typeof(TDestination);

    public bool IsIgnored { get; set; }

    public bool HasSourceField => SourceField is not null;

    public string DestinationFieldName => DestinationField.ToString();

    public void Invoke(Action<Expression, Expression?, Expression?, bool> action) {
        action.Invoke(DestinationField, SourceField, IgnoreCondition, IsIgnored);
    }

    #endregion

    internal void SetSourceField(Expression<Func<TSource, object?>> sourceField) {
        SourceField = sourceField;
    }

    internal void SetIgnoreCondition(Expression<Func<TSource, TDestination, bool>> condition) {
        IgnoreCondition = condition;
    }

    internal void SetIgnored(bool ignored = true) {
        IsIgnored       = ignored;
        SourceField     = null;
        IgnoreCondition = null;
    }
}
