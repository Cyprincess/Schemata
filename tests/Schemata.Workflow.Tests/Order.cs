using System;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Workflow.Skeleton.Entities;

namespace Schemata.Workflow.Tests;

public class Order : IConcurrency, IFreshness, IStatefulEntity
{
    #region IConcurrency Members

    public Guid? Timestamp { get; set; }

    #endregion

    #region IFreshness Members

    public string? EntityTag { get; set; }

    #endregion

    #region IStatefulEntity Members

    public long Id { get; set; }

    public string? State { get; set; }

    public DateTime? CreateTime { get; set; }

    public DateTime? UpdateTime { get; set; }

    #endregion
}
