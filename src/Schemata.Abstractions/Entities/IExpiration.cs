using System;

namespace Schemata.Abstractions.Entities;

public interface IExpiration
{
    DateTime? ExpireTime { get; set; }
}
