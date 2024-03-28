using System;

namespace Schemata.Abstractions.Entity;

public interface ITrash
{
    DateTime? DeletionDate { get; set; }
}
