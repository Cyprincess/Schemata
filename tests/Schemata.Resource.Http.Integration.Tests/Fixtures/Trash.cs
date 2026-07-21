using System;
using System.ComponentModel.DataAnnotations;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Http.Integration.Tests.Fixtures;

[CanonicalName("trashes/{trash}")]
[Microsoft.EntityFrameworkCore.PrimaryKey(nameof(Uid))]
public class Trash : IIdentifier, ICanonicalName, IConcurrency, IFreshness, IValidation, IUpdateMask, ISoftDelete
{
    public string? FullName { get; set; }
    public int     Age      { get; set; }
    public int     Grade    { get; set; }

    #region ICanonicalName Members

    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    #endregion

    #region IConcurrency Members

    [ConcurrencyCheck]
    public Guid Timestamp { get; set; }

    #endregion

    #region IFreshness Members

    public string? EntityTag { get; set; }

    #endregion

    #region IIdentifier Members

    public Guid Uid { get; set; }

    #endregion

    #region ISoftDelete Members

    public DateTime? DeleteTime { get; set; }

    public DateTime? PurgeTime { get; set; }

    #endregion

    #region IUpdateMask Members

    public string? UpdateMask { get; set; }

    #endregion

    #region IValidation Members

    public bool ValidateOnly { get; set; }

    #endregion
}
