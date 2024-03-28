using System;

namespace Schemata.Abstractions.Entities;

public interface ITrash
{
    DateTime? DeletionDate { get; set; }
}
