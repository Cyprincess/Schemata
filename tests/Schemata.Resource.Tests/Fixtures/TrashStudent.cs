using System;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Tests.Fixtures;

[CanonicalName("trashStudents/{trashStudent}")]
public class TrashStudent : Student, ISoftDelete
{
    #region ISoftDelete Members

    public DateTime? DeleteTime { get; set; }
    public DateTime? PurgeTime  { get; set; }

    #endregion
}
