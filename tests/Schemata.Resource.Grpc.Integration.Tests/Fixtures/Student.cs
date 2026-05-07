using System;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Entity.Repository;

namespace Schemata.Resource.Grpc.Integration.Tests.Fixtures;

[CanonicalName("students/{Student}")]
public class Student : IIdentifier, ICanonicalName, IConcurrency, IFreshness, IValidation, IUpdateMask
{
    public string? FullName { get; set; }
    public int     Age      { get; set; }
    public int     Grade    { get; set; }

    #region ICanonicalName Members

    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    #endregion

    #region IConcurrency Members

    public Guid? Timestamp { get; set; }

    #endregion

    #region IFreshness Members

    public string? EntityTag { get; set; }

    #endregion

    #region IIdentifier Members

    [TableKey]
    public Guid Uid { get; set; }

    #endregion

    #region IUpdateMask Members

    public string? UpdateMask { get; set; }

    #endregion

    #region IValidation Members

    public bool ValidateOnly { get; set; }

    #endregion
}
