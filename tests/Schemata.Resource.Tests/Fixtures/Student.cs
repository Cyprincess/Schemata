using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Tests.Fixtures;

[CanonicalName("students/{student}")]
[PrimaryKey(nameof(Uid))]
public class Student : IIdentifier, ICanonicalName, IConcurrency, IFreshness, IValidation, IUpdateMask, IAllowMissing
{
    public string? FullName { get; set; }
    public int     Age      { get; set; }
    public int     Grade    { get; set; }

    public StudentProfile? Profile { get; set; }

    public List<Course> Courses { get; set; } = [];

    #region IAllowMissing Members

    public bool AllowMissing { get; set; }

    #endregion

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

    #region IUpdateMask Members

    public string? UpdateMask { get; set; }

    #endregion

    #region IValidation Members

    public bool ValidateOnly { get; set; }

    #endregion
}