using System;

namespace Schemata.Abstractions.Resource;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class HttpResourceAttribute() : ResourceAttributeBase("HTTP");
