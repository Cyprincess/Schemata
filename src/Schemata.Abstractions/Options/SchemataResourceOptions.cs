using System;
using System.Collections.Generic;
using Schemata.Abstractions.Resource;

namespace Schemata.Abstractions.Options;

public class SchemataResourceOptions
{
    public Dictionary<Type, ResourceAttribute> Resources { get; } = [];
}
