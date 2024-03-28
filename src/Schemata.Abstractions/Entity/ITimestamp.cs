using System;

namespace Schemata.Abstractions.Entity;

public interface ITimestamp
{
    DateTime? CreationDate { get; set; }

    DateTime? ModificationDate { get; set; }
}
