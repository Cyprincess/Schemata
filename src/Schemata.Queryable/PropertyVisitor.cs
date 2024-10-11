using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

// ReSharper disable once CheckNamespace
namespace System.Linq;

public class PropertyVisitor : ExpressionVisitor
{
    private readonly List<PropertyInfo> _properties = [];
    private readonly Type               _type;

    public PropertyVisitor(Type type) {
        _type = type;
    }

    public IReadOnlyList<PropertyInfo> Properties => _properties;

    protected override Expression VisitMember(MemberExpression node) {
        var property = node.Member as PropertyInfo;
        if (property != null && _type.IsAssignableFrom(property.DeclaringType)) {
            _properties.Add(property);
        }

        return base.VisitMember(node);
    }
}
