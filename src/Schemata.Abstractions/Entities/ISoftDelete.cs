using System;

namespace Schemata.Abstractions.Entities;

public interface ISoftDelete
{
    DateTime? DeleteTime { get; set; }

    DateTime? PurgeTime { get; set; }
}
