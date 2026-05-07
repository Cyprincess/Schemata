using System;

namespace Schemata.Entity.Repository;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class TableKeyAttribute : Attribute
{
    public TableKeyAttribute(int order = 0) => Order = order;

    public int Order { get; }
}
