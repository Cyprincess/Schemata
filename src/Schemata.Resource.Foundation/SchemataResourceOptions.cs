using System;
using System.Collections.Generic;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Foundation;

public sealed class SchemataResourceOptions
{
    public Dictionary<RuntimeTypeHandle, ResourceAttribute> Resources { get; } = [];

    public bool SuppressCreateValidation { get; set; }

    public bool SuppressUpdateValidation { get; set; }

    public bool SuppressFreshness { get; set; }
}
