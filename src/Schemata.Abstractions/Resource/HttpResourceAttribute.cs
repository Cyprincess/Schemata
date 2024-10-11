using System;

namespace Schemata.Abstractions.Resource;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class HttpResourceAttribute : ResourceAttributeBase
{
    public HttpResourceAttribute() : base("HTTP") { }
}
