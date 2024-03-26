using System;

namespace Schemata.Core.Features;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public class DependsOnAttribute<T> : Attribute
    where T : class, ISimpleFeature
{ }
