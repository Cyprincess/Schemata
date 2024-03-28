using System;

namespace Schemata.Abstractions.Entities;

public interface ITimestamp
{
    DateTime? CreationDate { get; set; }

    DateTime? ModificationDate { get; set; }
}
